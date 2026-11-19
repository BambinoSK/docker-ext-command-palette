using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DockerExtension.Docker;

internal enum EndpointKind
{
    NamedPipe,
    Tcp,
    Unsupported,
}

internal sealed record DockerEndpoint(string Host, string Source, string? ContextName)
{
    public const string DefaultHost = "npipe:////./pipe/docker_engine";

    private const string ContextHostKey = "Host";

    public EndpointKind Kind =>
        Host.StartsWith("npipe:", StringComparison.OrdinalIgnoreCase) ? EndpointKind.NamedPipe
        : Host.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase) ? EndpointKind.Tcp
        : EndpointKind.Unsupported;

    // True when the CLI would not pick this endpoint on its own and needs an explicit -H.
    public bool RequiresExplicitCliHost => Source == SourceSetting;

    public const string SourceSetting = "extension settings";
    public const string SourceEnvironment = "DOCKER_HOST";
    public const string SourceContext = "docker context";
    public const string SourceDefault = "default";

    public (string Server, string PipeName) ParsePipe()
    {
        // npipe:////./pipe/docker_engine → server ".", pipe "docker_engine"
        var path = Host["npipe:".Length..].Replace('\\', '/').Trim('/');
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || !parts[1].Equals("pipe", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException($"Invalid named pipe address: {Host}");
        }

        return (parts[0], string.Join('/', parts[2..]));
    }

    public Uri ParseTcp()
    {
        var uri = new Uri(Host);
        var useTls = Environment.GetEnvironmentVariable("DOCKER_TLS_VERIFY") is { Length: > 0 } v && v != "0";
        var builder = new UriBuilder(useTls ? "https" : "http", uri.Host, uri.Port == -1 ? (useTls ? 2376 : 2375) : uri.Port);
        return builder.Uri;
    }

    public static DockerEndpoint Resolve(string? overrideHost)
    {
        if (!string.IsNullOrWhiteSpace(overrideHost))
        {
            return new DockerEndpoint(overrideHost.Trim(), SourceSetting, null);
        }

        var envHost = Environment.GetEnvironmentVariable("DOCKER_HOST");
        if (!string.IsNullOrWhiteSpace(envHost))
        {
            return new DockerEndpoint(envHost.Trim(), SourceEnvironment, null);
        }

        var context = CurrentContextName();
        if (!string.IsNullOrEmpty(context) && context != "default")
        {
            var host = ReadContextHost(context);
            if (host is not null)
            {
                return new DockerEndpoint(host, SourceContext, context);
            }
        }

        return new DockerEndpoint(DefaultHost, SourceDefault, "default");
    }

    private static string ConfigDirectory() =>
        Environment.GetEnvironmentVariable("DOCKER_CONFIG") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".docker");

    private static string? CurrentContextName()
    {
        var env = Environment.GetEnvironmentVariable("DOCKER_CONTEXT");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env.Trim();
        }

        try
        {
            var configPath = Path.Combine(ConfigDirectory(), "config.json");
            if (!File.Exists(configPath))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            return doc.RootElement.TryGetProperty("currentContext", out var ctx) ? ctx.GetString() : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? ReadContextHost(string context)
    {
        try
        {
            // The CLI stores each context under the hex SHA-256 of its name.
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(context)));
            var metaPath = Path.Combine(ConfigDirectory(), "contexts", "meta", hash, "meta.json");
            if (!File.Exists(metaPath))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(metaPath));
            if (doc.RootElement.TryGetProperty("Endpoints", out var endpoints) &&
                endpoints.TryGetProperty("docker", out var docker) &&
                docker.TryGetProperty(ContextHostKey, out var host))
            {
                return host.GetString();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }

        return null;
    }
}
