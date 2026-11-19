using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DockerExtension.Docker;

internal enum DockerResource
{
    Containers,
    Images,
    Volumes,
    Networks,
    Engine,
}

/// <summary>
/// Process-wide connection to the engine: owns the client, caches the container list
/// (used by every page and the fallback), and keeps it current from the /events stream.
/// </summary>
internal sealed partial class DockerService : IDisposable
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromSeconds(30);

    private readonly Lock _gate = new();
    private readonly Timer _debounce;
    private DockerClient? _client;
    private CancellationTokenSource? _monitorCts;
    private HashSet<DockerResource> _pendingChanges = [];
    private int _refreshing;
    private int _refreshAgain;

    private DockerService()
    {
        _debounce = new Timer(_ => FlushPendingChanges());
    }

    public static DockerService Instance { get; } = new();

    /// <summary>Raised on a thread-pool thread when cached or engine-side data changed.</summary>
    public event Action<DockerResource>? Changed;

    public IReadOnlyList<ContainerSummary> Containers { get; private set; } = [];

    public bool HasLoadedContainers { get; private set; }

    public string? LastError { get; private set; }

    public bool IsConnected => LastError is null && HasLoadedContainers;

    public string EngineOsType { get; private set; } = "linux";

    public DockerEndpoint Endpoint => Client.Endpoint;

    public DockerClient Client
    {
        get
        {
            lock (_gate)
            {
                return _client ??= new DockerClient(DockerEndpoint.Resolve(SettingsManager.Instance.EndpointOverride));
            }
        }
    }

    /// <summary>Starts the event monitor once; later calls are no-ops.</summary>
    public void EnsureStarted()
    {
        CancellationToken token;
        lock (_gate)
        {
            if (_monitorCts is not null)
            {
                return;
            }

            _monitorCts = new CancellationTokenSource();
            token = _monitorCts.Token;
        }

        _ = Task.Run(() => MonitorLoopAsync(token), token);
    }

    /// <summary>Drops the client and re-resolves the endpoint (context or settings changed).</summary>
    public void Reset()
    {
        DockerClient? oldClient;
        CancellationTokenSource? oldCts;
        lock (_gate)
        {
            oldClient = _client;
            oldCts = _monitorCts;
            _client = null;
            _monitorCts = null;
            Containers = [];
            HasLoadedContainers = false;
            LastError = null;
        }

        oldCts?.Cancel();
        oldCts?.Dispose();
        oldClient?.Dispose();

        RaiseChanged(DockerResource.Engine);
        RaiseChanged(DockerResource.Containers);
        EnsureStarted();
    }

    public void RequestRefresh(DockerResource resource)
    {
        lock (_gate)
        {
            _pendingChanges.Add(resource);
        }

        _debounce.Change(DebounceDelay, Timeout.InfiniteTimeSpan);
    }

    public async Task RefreshContainersAsync()
    {
        // A request that arrives mid-refresh re-runs the fetch so the newest state is never skipped.
        if (Interlocked.Exchange(ref _refreshing, 1) == 1)
        {
            Volatile.Write(ref _refreshAgain, 1);
            return;
        }

        try
        {
            do
            {
                Volatile.Write(ref _refreshAgain, 0);
                try
                {
                    Containers = await Client.ListContainersAsync(CancellationToken.None).ConfigureAwait(false);
                    HasLoadedContainers = true;
                    LastError = null;
                }
                catch (Exception ex) when (ex is DockerUnavailableException or DockerApiException or ObjectDisposedException)
                {
                    Containers = [];
                    LastError = ex.Message;
                }
            }
            while (Volatile.Read(ref _refreshAgain) == 1);
        }
        finally
        {
            Interlocked.Exchange(ref _refreshing, 0);
        }

        RaiseChanged(DockerResource.Containers);
    }

    public void Dispose()
    {
        _monitorCts?.Cancel();
        _monitorCts?.Dispose();
        _debounce.Dispose();
        _client?.Dispose();
    }

    private async Task MonitorLoopAsync(CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(2);
        while (!ct.IsCancellationRequested)
        {
            await RefreshContainersAsync().ConfigureAwait(false);
            if (LastError is null)
            {
                delay = TimeSpan.FromSeconds(2);
                await LoadEngineInfoAsync(ct).ConfigureAwait(false);
                RaiseChanged(DockerResource.Engine);

                try
                {
                    await foreach (var evt in Client.StreamEventsAsync(ct).ConfigureAwait(false))
                    {
                        RequestRefresh(evt.Type switch
                        {
                            "image" => DockerResource.Images,
                            "volume" => DockerResource.Volumes,
                            "network" => DockerResource.Networks,
                            _ => DockerResource.Containers,
                        });
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex) when (ex is DockerUnavailableException or DockerApiException or System.IO.IOException or System.Net.Http.HttpRequestException or ObjectDisposedException)
                {
                    LastError = ex.Message;
                }

                // The stream ended: the engine stopped or restarted.
                RaiseChanged(DockerResource.Engine);
            }

            try
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxReconnectDelay.Ticks));
        }
    }

    private async Task LoadEngineInfoAsync(CancellationToken ct)
    {
        try
        {
            var info = await Client.GetInfoAsync(ct).ConfigureAwait(false);
            EngineOsType = string.IsNullOrEmpty(info.OSType) ? "linux" : info.OSType;
        }
        catch (Exception ex) when (ex is DockerUnavailableException or DockerApiException)
        {
        }
    }

    private void FlushPendingChanges()
    {
        HashSet<DockerResource> changes;
        lock (_gate)
        {
            changes = _pendingChanges;
            _pendingChanges = [];
        }

        if (changes.Contains(DockerResource.Containers))
        {
            // Raises Containers itself once the new list is cached.
            _ = RefreshContainersAsync();
            changes.Remove(DockerResource.Containers);
        }

        foreach (var change in changes)
        {
            RaiseChanged(change);
        }
    }

    private void RaiseChanged(DockerResource resource)
    {
        try
        {
            Changed?.Invoke(resource);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Docker change handler failed: {ex}");
        }
    }
}
