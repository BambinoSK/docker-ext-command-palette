using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DockerExtension.Commands;
using DockerExtension.Docker;
using DockerExtension.Helpers;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DockerExtension.Pages;

/// <summary>Engine status, docker contexts, disk usage and clean-up.</summary>
internal sealed partial class SystemPage : ListPage
{
    private readonly Lock _gate = new();
    private SystemInfo? _info;
    private SystemVersion? _version;
    private SystemDf? _df;
    private List<ContextEntry>? _contexts;
    private bool _stale = true;
    private bool _loading;

    public SystemPage()
    {
        Icon = Icons.Engine;
        Title = "Docker system";
        Name = "Open";
        PlaceholderText = "Engine, contexts, disk usage and clean-up";

        DockerService.Instance.Changed += changed =>
        {
            if (changed is DockerResource.Engine or DockerResource.Images or DockerResource.Volumes)
            {
                _stale = true;
                RaiseItemsChanged();
            }
        };
    }

    public override IListItem[] GetItems()
    {
        ShowDetails = SettingsManager.Instance.ShowDetails;
        var service = DockerService.Instance;
        service.EnsureStarted();
        if (_stale)
        {
            StartLoad();
        }

        var items = new List<IListItem>();
        if (service.LastError is { } error)
        {
            items.AddRange(StatusItems.EngineUnavailable(error).Cast<ListItem>().Select(i => WithSection(i, "Engine")));
        }
        else if (_info is { } info)
        {
            items.Add(EngineItem(info, _version));
        }

        if (_contexts is { Count: > 0 } contexts)
        {
            items.AddRange(contexts.Select(ContextItem));
        }

        if (service.LastError is null)
        {
            items.AddRange(DiskItems(_df));
            items.Add(WithSection(new ListItem(SystemPruneCommand()) { Title = "Clean up everything unused", Subtitle = "Stopped containers, unused networks, dangling images and build cache", Icon = Icons.Prune }, "Clean up"));
        }

        return [.. items];
    }

    private static ListItem WithSection(ListItem item, string section)
    {
        item.Section = section;
        return item;
    }

    private void StartLoad()
    {
        lock (_gate)
        {
            if (_loading)
            {
                return;
            }

            _loading = true;
            _stale = false;
        }

        IsLoading = true;
        _ = Task.Run(async () =>
        {
            var client = DockerService.Instance.Client;
            var contextsTask = LoadContextsAsync();
            try
            {
                _info = await client.GetInfoAsync(CancellationToken.None).ConfigureAwait(false);
                _version = await client.GetVersionAsync(CancellationToken.None).ConfigureAwait(false);
                RaiseItemsChanged();
                _df = await client.GetDiskUsageAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is DockerUnavailableException or DockerApiException or ObjectDisposedException)
            {
                _info = null;
                _df = null;
            }

            _contexts = await contextsTask.ConfigureAwait(false);
            lock (_gate)
            {
                _loading = false;
            }

            IsLoading = false;
            RaiseItemsChanged();
        });
    }

    private static async Task<List<ContextEntry>?> LoadContextsAsync()
    {
        try
        {
            var result = await DockerCli.RunAsync(["context", "ls", "--format", "json"], null, TimeSpan.FromSeconds(15), CancellationToken.None).ConfigureAwait(false);
            return result.Succeeded ? ContextEntry.Parse(result.Output) : null;
        }
        catch (Exception ex) when (ex is DockerUnavailableException or TimeoutException or InvalidOperationException)
        {
            return null;
        }
    }

    private static ListItem EngineItem(SystemInfo info, SystemVersion? version)
    {
        var endpoint = DockerService.Instance.Endpoint;
        var metadata = new List<IDetailsElement>
        {
            StatusItems.Text("Endpoint", endpoint.Host),
            StatusItems.Text("Selected by", endpoint.Source + (endpoint.ContextName is { } ctx ? $" ({ctx})" : string.Empty)),
            StatusItems.Text("Engine", info.ServerVersion),
            StatusItems.Text("API version", version?.ApiVersion),
            StatusItems.Text("Platform", version?.Platform?.Name),
            StatusItems.Text("OS", $"{info.OperatingSystem} ({info.OSType}/{info.Architecture})"),
            StatusItems.Text("Kernel", version?.KernelVersion),
            StatusItems.Text("CPUs", info.NCPU.ToString(System.Globalization.CultureInfo.CurrentCulture)),
            StatusItems.Text("Memory", Format.Bytes(info.MemTotal)),
            StatusItems.Separator(),
            StatusItems.Text("Containers", $"{info.Containers} ({info.ContainersRunning} running, {info.ContainersPaused} paused, {info.ContainersStopped} stopped)"),
            StatusItems.Text("Images", info.Images.ToString(System.Globalization.CultureInfo.CurrentCulture)),
        };

        return WithSection(
            new ListItem(ResourceCommands.Refresh(DockerResource.Engine))
            {
                Title = $"Docker Engine {info.ServerVersion}",
                Subtitle = $"{info.OperatingSystem} · {info.NCPU} CPUs · {Format.Bytes(info.MemTotal)} memory",
                Icon = Icons.Engine,
                Tags = [StatusItems.StateTag("running")],
                MoreCommands =
                [
                    new CommandContextItem(new CopyTextCommand(endpoint.Host) { Name = "Copy endpoint" }) { Icon = Icons.Copy },
                    new CommandContextItem(SettingsManager.Instance.Settings.SettingsPage) { Title = "Extension settings", Icon = Icons.Settings },
                ],
                Details = new Details { Title = $"Docker Engine {info.ServerVersion}", Metadata = [.. metadata] },
            },
            "Engine");
    }

    private static ListItem ContextItem(ContextEntry context)
    {
        var command = new ActionCommand("Switch", Icons.Context, () =>
            Toast.Run(
                async () =>
                {
                    var result = await DockerCli.RunAsync(["context", "use", context.Name], null, TimeSpan.FromSeconds(15), CancellationToken.None).ConfigureAwait(false);
                    if (!result.Succeeded)
                    {
                        throw new InvalidOperationException(result.Message);
                    }

                    DockerService.Instance.Reset();
                    return SettingsManager.Instance.EndpointOverride.Length > 0
                        ? $"Switched the CLI to {context.Name}. The extension keeps using the Docker host from its settings."
                        : $"Switched to {context.Name}";
                },
                $"Couldn't switch to {context.Name}",
                DockerResource.Engine,
                $"Switching to {context.Name}…"));

        return WithSection(
            new ListItem(context.Current ? new NoOpCommand() : command)
            {
                Title = context.Name,
                Subtitle = context.Endpoint,
                Icon = Icons.Context,
                Tags = context.Current ? [StatusItems.Colored("current", 0x2E, 0xA0, 0x43)] : [],
                Details = new Details
                {
                    Title = context.Name,
                    Body = Format.Md(context.Description),
                    Metadata = [StatusItems.Text("Endpoint", context.Endpoint), StatusItems.Text("Current", context.Current ? "Yes" : "No")],
                },
            },
            "Contexts");
    }

    private static IEnumerable<IListItem> DiskItems(SystemDf? df)
    {
        if (df is null)
        {
            yield return WithSection(new ListItem(new NoOpCommand()) { Title = "Calculating disk usage…", Icon = Icons.Disk }, "Disk usage");
            yield break;
        }

        var images = df.Images ?? [];
        var imagesReclaimable = images.Where(i => i.Containers == 0).Sum(i => Math.Max(0, i.Size - Math.Max(0, i.SharedSize)));
        yield return DiskItem(
            "Images",
            df.LayersSize,
            imagesReclaimable,
            Format.Plural(images.Count, "image"),
            ResourceCommands.Prune("Remove all unused images", "unused images", "Every image that no container uses is deleted, including tagged ones.", DockerResource.Images, (c, ct) => c.PruneImagesAsync(allUnused: true, ct)));

        var containers = df.Containers ?? [];
        yield return DiskItem(
            "Containers",
            containers.Sum(c => c.SizeRw),
            containers.Where(c => c.State != "running" && c.State != "paused").Sum(c => c.SizeRw),
            Format.Plural(containers.Count, "container"),
            ResourceCommands.Prune("Remove stopped containers", "stopped containers", "Every container that is not running is deleted.", DockerResource.Containers, (c, ct) => c.PruneContainersAsync(ct)));

        var volumes = df.Volumes ?? [];
        yield return DiskItem(
            "Volumes",
            volumes.Sum(v => Math.Max(0, v.UsageData?.Size ?? 0)),
            volumes.Where(v => v.UsageData?.RefCount == 0).Sum(v => Math.Max(0, v.UsageData?.Size ?? 0)),
            Format.Plural(volumes.Count, "volume"),
            ResourceCommands.Prune("Remove unused volumes", "unused volumes", "Every volume that no container uses is deleted, including named ones. The data in them is lost.", DockerResource.Volumes, (c, ct) => c.PruneVolumesAsync(ct)));

        var cache = df.BuildCache ?? [];
        yield return DiskItem(
            "Build cache",
            cache.Sum(b => b.Size),
            cache.Where(b => !b.InUse && !b.Shared).Sum(b => b.Size),
            Format.Plural(cache.Count, "entry"),
            ResourceCommands.Prune("Clear build cache", "build cache", "All unused build cache is deleted. The next builds will be slower.", DockerResource.Engine, (c, ct) => c.PruneBuildCacheAsync(ct)));
    }

    private static ListItem DiskItem(string title, long total, long reclaimable, string count, InvokableCommand prune) =>
        WithSection(
            new ListItem(prune)
            {
                Title = title,
                Subtitle = $"{Format.Bytes(total)} · {Format.Bytes(reclaimable)} reclaimable · {count}",
                Icon = Icons.Disk,
                Details = new Details
                {
                    Title = title,
                    Metadata =
                    [
                        StatusItems.Text("Total", Format.Bytes(total)),
                        StatusItems.Text("Reclaimable", Format.Bytes(reclaimable)),
                        StatusItems.Text("Count", count),
                    ],
                    Body = $"Press **Enter** to {prune.Name.ToLowerInvariant()}.",
                },
            },
            "Disk usage");

    private static InvokableCommand SystemPruneCommand()
    {
        var inner = ResourceCommands.Background("Clean up", Icons.Prune, "Cleaning up…", "Clean-up failed", DockerResource.Engine, async (client, ct) =>
        {
            var results = new[]
            {
                await client.PruneContainersAsync(ct).ConfigureAwait(false),
                await client.PruneNetworksAsync(ct).ConfigureAwait(false),
                await client.PruneImagesAsync(allUnused: false, ct).ConfigureAwait(false),
                await client.PruneBuildCacheAsync(ct).ConfigureAwait(false),
            };
            DockerService.Instance.RequestRefresh(DockerResource.Containers);
            DockerService.Instance.RequestRefresh(DockerResource.Images);
            DockerService.Instance.RequestRefresh(DockerResource.Networks);
            return $"Removed {Format.Plural(results.Sum(r => r.DeletedCount), "item")}, reclaimed {Format.Bytes(results.Sum(r => r.SpaceReclaimed))}";
        });

        return ResourceCommands.Confirmed(
            inner,
            "Clean up everything unused?",
            "Deletes stopped containers, unused networks, dangling images and the build cache. Volumes are kept.");
    }

    internal sealed record ContextEntry(string Name, string Endpoint, string? Description, bool Current)
    {
        // Newer CLIs print one JSON object per line; older ones print a JSON array.
        public static List<ContextEntry> Parse(string output)
        {
            var list = new List<ContextEntry>();
            var trimmed = output.Trim();
            if (trimmed.StartsWith('['))
            {
                using var doc = JsonDocument.Parse(trimmed);
                list.AddRange(doc.RootElement.EnumerateArray().Select(FromElement));
                return list;
            }

            foreach (var line in trimmed.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    list.Add(FromElement(doc.RootElement));
                }
                catch (JsonException)
                {
                }
            }

            return list;
        }

        private const string NameKey = "Name";
        private const string EndpointKey = "DockerEndpoint";
        private const string DescriptionKey = "Description";
        private const string CurrentKey = "Current";

        private static ContextEntry FromElement(JsonElement e) => new(
            Str(e, NameKey) ?? "?",
            Str(e, EndpointKey) ?? string.Empty,
            Str(e, DescriptionKey),
            e.TryGetProperty(CurrentKey, out var current) && current.ValueKind == JsonValueKind.True);

        private static string? Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }
}
