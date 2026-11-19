using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace DockerExtension.Docker;

/// <summary>Minimal Docker Engine API client over a named pipe or TCP.</summary>
internal sealed partial class DockerClient : IDisposable
{
    private static readonly TimeSpan PipeConnectTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private readonly HttpClient _streamingHttp;

    public DockerClient(DockerEndpoint endpoint)
    {
        Endpoint = endpoint;
        var baseAddress = endpoint.Kind switch
        {
            EndpointKind.NamedPipe => new Uri("http://docker/"),
            EndpointKind.Tcp => endpoint.ParseTcp(),
            _ => throw new DockerUnavailableException($"Unsupported Docker host '{endpoint.Host}'. Only npipe:// and tcp:// endpoints are supported."),
        };

        _http = new HttpClient(CreateHandler(endpoint)) { BaseAddress = baseAddress, Timeout = RequestTimeout };
        _streamingHttp = new HttpClient(CreateHandler(endpoint)) { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan };
    }

    public DockerEndpoint Endpoint { get; }

    public void Dispose()
    {
        _http.Dispose();
        _streamingHttp.Dispose();
    }

    // ── System ────────────────────────────────────────────────────────────────

    public Task<SystemVersion> GetVersionAsync(CancellationToken ct) => GetAsync("version", Json.SystemVersion, ct);

    public Task<SystemInfo> GetInfoAsync(CancellationToken ct) => GetAsync("info", Json.SystemInfo, ct);

    public Task<SystemDf> GetDiskUsageAsync(CancellationToken ct) => GetAsync("system/df", Json.SystemDf, ct, streaming: true);

    // ── Containers ────────────────────────────────────────────────────────────

    public Task<List<ContainerSummary>> ListContainersAsync(CancellationToken ct) =>
        GetAsync("containers/json?all=true", Json.ListContainerSummary, ct);

    public Task<ContainerInspect> InspectContainerAsync(string id, CancellationToken ct) =>
        GetAsync($"containers/{Esc(id)}/json", Json.ContainerInspect, ct);

    // stream=false without one-shot waits for a second sample so CPU usage can be computed.
    public Task<ContainerStats> GetContainerStatsAsync(string id, CancellationToken ct) =>
        GetAsync($"containers/{Esc(id)}/stats?stream=false", Json.ContainerStats, ct);

    public Task StartContainerAsync(string id, CancellationToken ct) => SendAsync(HttpMethod.Post, $"containers/{Esc(id)}/start", ct);

    public Task StopContainerAsync(string id, CancellationToken ct) => SendAsync(HttpMethod.Post, $"containers/{Esc(id)}/stop", ct, streaming: true);

    public Task RestartContainerAsync(string id, CancellationToken ct) => SendAsync(HttpMethod.Post, $"containers/{Esc(id)}/restart", ct, streaming: true);

    public Task PauseContainerAsync(string id, CancellationToken ct) => SendAsync(HttpMethod.Post, $"containers/{Esc(id)}/pause", ct);

    public Task UnpauseContainerAsync(string id, CancellationToken ct) => SendAsync(HttpMethod.Post, $"containers/{Esc(id)}/unpause", ct);

    public Task KillContainerAsync(string id, CancellationToken ct) => SendAsync(HttpMethod.Post, $"containers/{Esc(id)}/kill", ct);

    public Task RemoveContainerAsync(string id, bool force, CancellationToken ct) =>
        SendAsync(HttpMethod.Delete, $"containers/{Esc(id)}?force={(force ? "true" : "false")}", ct);

    public Task<PruneResponse> PruneContainersAsync(CancellationToken ct) =>
        SendAsync(HttpMethod.Post, "containers/prune", Json.PruneResponse, ct);

    // ── Images ────────────────────────────────────────────────────────────────

    public Task<List<ImageSummary>> ListImagesAsync(CancellationToken ct) => GetAsync("images/json", Json.ListImageSummary, ct);

    public Task RemoveImageAsync(string id, bool force, CancellationToken ct) =>
        SendAsync(HttpMethod.Delete, $"images/{Esc(id)}?force={(force ? "true" : "false")}", ct);

    public Task<PruneResponse> PruneImagesAsync(bool allUnused, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, "images/prune" + (allUnused ? "?filters=" + Uri.EscapeDataString("""{"dangling":["false"]}""") : string.Empty), Json.PruneResponse, ct, streaming: true);

    public async Task PullImageAsync(string reference, CancellationToken ct)
    {
        var (name, tag) = SplitReference(reference);
        var path = $"images/create?fromImage={Uri.EscapeDataString(name)}&tag={Uri.EscapeDataString(tag)}";
        await foreach (var progress in StreamJsonAsync(HttpMethod.Post, path, Json.StreamProgress, ct).ConfigureAwait(false))
        {
            if (!string.IsNullOrEmpty(progress.Error))
            {
                throw new DockerApiException(HttpStatusCode.InternalServerError, progress.Error);
            }
        }
    }

    // ── Volumes ───────────────────────────────────────────────────────────────

    public async Task<List<Volume>> ListVolumesAsync(CancellationToken ct) =>
        (await GetAsync("volumes", Json.VolumeListResponse, ct).ConfigureAwait(false)).Volumes ?? [];

    public Task RemoveVolumeAsync(string name, CancellationToken ct) => SendAsync(HttpMethod.Delete, $"volumes/{Esc(name)}", ct);

    // all=true includes named volumes; without it the engine prunes only anonymous ones.
    public Task<PruneResponse> PruneVolumesAsync(CancellationToken ct) =>
        SendAsync(HttpMethod.Post, "volumes/prune?filters=" + Uri.EscapeDataString("""{"all":["true"]}"""), Json.PruneResponse, ct, streaming: true);

    // ── Networks ──────────────────────────────────────────────────────────────

    public Task<List<NetworkSummary>> ListNetworksAsync(CancellationToken ct) => GetAsync("networks", Json.ListNetworkSummary, ct);

    public Task RemoveNetworkAsync(string id, CancellationToken ct) => SendAsync(HttpMethod.Delete, $"networks/{Esc(id)}", ct);

    public Task<PruneResponse> PruneNetworksAsync(CancellationToken ct) =>
        SendAsync(HttpMethod.Post, "networks/prune", Json.PruneResponse, ct);

    public Task<PruneResponse> PruneBuildCacheAsync(CancellationToken ct) =>
        SendAsync(HttpMethod.Post, "build/prune", Json.PruneResponse, ct, streaming: true);

    // ── Events ────────────────────────────────────────────────────────────────

    public IAsyncEnumerable<DockerEvent> StreamEventsAsync(CancellationToken ct)
    {
        var filters = Uri.EscapeDataString("""{"type":["container","image","volume","network"]}""");
        return StreamJsonAsync(HttpMethod.Get, $"events?filters={filters}", Json.DockerEvent, ct);
    }

    // ── Plumbing ──────────────────────────────────────────────────────────────

    private static DockerJsonContext Json => DockerJsonContext.Default;

    private static string Esc(string segment) => Uri.EscapeDataString(segment);

    private static (string Name, string Tag) SplitReference(string reference)
    {
        var at = reference.IndexOf('@', StringComparison.Ordinal);
        if (at >= 0)
        {
            return (reference[..at], reference[(at + 1)..]);
        }

        // A colon after the last slash separates the tag; earlier colons belong to a registry port.
        var lastSlash = reference.LastIndexOf('/');
        var colon = reference.LastIndexOf(':');
        return colon > lastSlash ? (reference[..colon], reference[(colon + 1)..]) : (reference, "latest");
    }

    private async Task<T> GetAsync<T>(string path, JsonTypeInfo<T> type, CancellationToken ct, bool streaming = false)
    {
        using var response = await SendCoreAsync(HttpMethod.Get, path, streaming, ct).ConfigureAwait(false);
        var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            return await JsonSerializer.DeserializeAsync(stream, type, ct).ConfigureAwait(false)
                ?? throw new DockerApiException(response.StatusCode, $"Empty response from {path}");
        }
    }

    private async Task SendAsync(HttpMethod method, string path, CancellationToken ct, bool streaming = false)
    {
        using var response = await SendCoreAsync(method, path, streaming, ct).ConfigureAwait(false);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, JsonTypeInfo<T> type, CancellationToken ct, bool streaming = false)
    {
        using var response = await SendCoreAsync(method, path, streaming, ct).ConfigureAwait(false);
        var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            return await JsonSerializer.DeserializeAsync(stream, type, ct).ConfigureAwait(false)
                ?? throw new DockerApiException(response.StatusCode, $"Empty response from {path}");
        }
    }

    private async IAsyncEnumerable<T> StreamJsonAsync<T>(HttpMethod method, string path, JsonTypeInfo<T> type, [EnumeratorCancellation] CancellationToken ct)
    {
        using var response = await SendCoreAsync(method, path, streaming: true, ct, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
            {
                if (line.Length == 0)
                {
                    continue;
                }

                T? item;
                try
                {
                    item = JsonSerializer.Deserialize(line, type);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (item is not null)
                {
                    yield return item;
                }
            }
        }
    }

    private async Task<HttpResponseMessage> SendCoreAsync(
        HttpMethod method,
        string path,
        bool streaming,
        CancellationToken ct,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        var client = streaming ? _streamingHttp : _http;
        using var request = new HttpRequestMessage(method, path);
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, completion, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new DockerUnavailableException(DescribeConnectionFailure(ex), ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new DockerUnavailableException($"Docker engine at {Endpoint.Host} did not respond in time.", ex);
        }

        // 304 means the container was already in the requested state.
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotModified)
        {
            return response;
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            string? message = null;
            try
            {
                message = JsonSerializer.Deserialize(body, Json.ErrorResponse)?.Message;
            }
            catch (JsonException)
            {
            }

            throw new DockerApiException(response.StatusCode, message ?? $"{(int)response.StatusCode} {response.ReasonPhrase}");
        }
    }

    private string DescribeConnectionFailure(HttpRequestException ex)
    {
        var inner = ex.InnerException;
        return inner switch
        {
            TimeoutException or FileNotFoundException => $"Docker engine is not running (no pipe at {Endpoint.Host}).",
            UnauthorizedAccessException => $"Access to {Endpoint.Host} was denied. Add your account to the 'docker-users' group and sign in again.",
            SocketException se when se.SocketErrorCode == SocketError.ConnectionRefused => $"Docker engine refused the connection at {Endpoint.Host}.",
            _ => $"Cannot reach Docker engine at {Endpoint.Host}: {inner?.Message ?? ex.Message}",
        };
    }

    private static SocketsHttpHandler CreateHandler(DockerEndpoint endpoint)
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
            UseProxy = false,
        };

        if (endpoint.Kind == EndpointKind.NamedPipe)
        {
            var (server, pipeName) = endpoint.ParsePipe();
            handler.ConnectCallback = async (_, ct) =>
            {
                var pipe = new NamedPipeClientStream(server, pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                try
                {
                    await pipe.ConnectAsync(PipeConnectTimeout, ct).ConfigureAwait(false);
                    return pipe;
                }
                catch
                {
                    await pipe.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            };
        }
        else if (endpoint.Kind == EndpointKind.Tcp && endpoint.ParseTcp().Scheme == Uri.UriSchemeHttps)
        {
            ConfigureTls(handler);
        }

        return handler;
    }

    // Mirrors the CLI: DOCKER_CERT_PATH holds ca.pem, cert.pem and key.pem.
    private static void ConfigureTls(SocketsHttpHandler handler)
    {
        var certDir = Environment.GetEnvironmentVariable("DOCKER_CERT_PATH") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".docker");

        var certPath = Path.Combine(certDir, "cert.pem");
        var keyPath = Path.Combine(certDir, "key.pem");
        var caPath = Path.Combine(certDir, "ca.pem");

        if (File.Exists(certPath) && File.Exists(keyPath))
        {
            // SChannel needs the key in a PKCS#12 container rather than an ephemeral PEM key.
            using var pem = X509Certificate2.CreateFromPemFile(certPath, keyPath);
            var clientCert = X509CertificateLoader.LoadPkcs12(pem.Export(X509ContentType.Pkcs12), null);
            handler.SslOptions.ClientCertificates = [clientCert];
        }

        if (File.Exists(caPath))
        {
            var ca = X509CertificateLoader.LoadCertificateFromFile(caPath);
            handler.SslOptions.RemoteCertificateValidationCallback = (_, cert, _, errors) =>
            {
                if (errors == System.Net.Security.SslPolicyErrors.None)
                {
                    return true;
                }

                if (cert is null || (errors & System.Net.Security.SslPolicyErrors.RemoteCertificateNameMismatch) != 0)
                {
                    return false;
                }

                using var chain = new X509Chain();
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(ca);
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                using var serverCert = X509CertificateLoader.LoadCertificate(cert.GetRawCertData());
                return chain.Build(serverCert);
            };
        }
    }
}
