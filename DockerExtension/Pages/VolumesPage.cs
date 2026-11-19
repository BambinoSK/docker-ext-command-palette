using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DockerExtension.Commands;
using DockerExtension.Docker;
using DockerExtension.Helpers;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Windows.System;

namespace DockerExtension.Pages;

internal sealed partial class VolumesPage : ResourceListPage<Volume>
{
    private const string FilterInUse = "inuse";
    private const string FilterUnused = "unused";

    private readonly SimpleFilters _filters;

    public VolumesPage()
        : base(DockerResource.Volumes)
    {
        Icon = Icons.Volume;
        Title = "Docker volumes";
        Name = "Open";
        PlaceholderText = "Search volumes";
        EmptyContent = new CommandItem(new NoOpCommand()) { Title = "No volumes", Subtitle = "Nothing matches this filter", Icon = Icons.Volume };
        _filters = new SimpleFilters(
            () => RaiseItemsChanged(),
            ("all", "All", Icons.All),
            (FilterInUse, "In use", Icons.Start),
            (FilterUnused, "Unused", Icons.Stop));
        Filters = _filters;
    }

    protected override async Task<IReadOnlyList<Volume>> LoadAsync(DockerClient client, CancellationToken ct) =>
        await client.ListVolumesAsync(ct).ConfigureAwait(false);

    protected override IListItem[] BuildItems(IReadOnlyList<Volume> volumes)
    {
        var usage = DockerService.Instance.Containers
            .SelectMany(c => (c.Mounts ?? []).Where(m => m.Type == "volume" && m.Name is not null).Select(m => (Volume: m.Name!, Container: c.DisplayName())))
            .GroupBy(x => x.Volume)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Container).Distinct().ToList());

        var rows = volumes
            .Select(v => (Volume: v, UsedBy: usage.GetValueOrDefault(v.Name) ?? []))
            .Where(r => _filters.CurrentFilterId switch
            {
                FilterInUse => r.UsedBy.Count > 0,
                FilterUnused => r.UsedBy.Count == 0,
                _ => true,
            })
            .OrderBy(r => IsAnonymous(r.Volume.Name) ? 1 : 0)
            .ThenBy(r => r.Volume.Name, System.StringComparer.OrdinalIgnoreCase)
            .Select(r => (IListItem)BuildItem(r.Volume, r.UsedBy))
            .ToList();

        var prune = ResourceCommands.Prune(
            "Remove unused volumes",
            "unused volumes",
            "Every volume that no container uses is deleted, including named ones. The data in them is lost.",
            DockerResource.Volumes,
            (client, ct) => client.PruneVolumesAsync(ct));
        rows.Add(new ListItem(prune) { Title = prune.Name, Icon = Icons.Prune, Section = "Clean up" });
        return [.. rows];
    }

    private static bool IsAnonymous(string name) => name.Length == 64 && HexName().IsMatch(name);

    private static ListItem BuildItem(Volume volume, List<string> usedBy)
    {
        var title = IsAnonymous(volume.Name) ? $"{volume.Name[..12]} (anonymous)" : volume.Name;
        var specs = new List<CommandSpec>
        {
            new(new CopyTextCommand(volume.Name) { Name = "Copy name" }, "Copy name", Icons.Copy),
        };

        if (!string.IsNullOrEmpty(volume.Mountpoint))
        {
            specs.Add(new CommandSpec(new CopyTextCommand(volume.Mountpoint) { Name = "Copy mount point" }, "Copy mount point", Icons.Copy));
        }

        var remove = ResourceCommands.Background("Remove volume", Icons.Delete, $"Removing {title}…", $"Couldn't remove {title}", DockerResource.Volumes, async (client, ct) =>
        {
            await client.RemoveVolumeAsync(volume.Name, ct).ConfigureAwait(false);
            return $"Removed {title}";
        });
        specs.Add(new CommandSpec(
            ResourceCommands.Confirmed(remove, $"Remove {title}?", "The volume and all data in it are deleted."),
            "Remove volume",
            Icons.Delete,
            CommandSpec.Ctrl(VirtualKey.Delete),
            Critical: true));

        var subtitle = $"{volume.Driver} · " + (usedBy.Count > 0 ? $"used by {string.Join(", ", usedBy)}" : "unused");
        var metadata = new List<IDetailsElement>
        {
            StatusItems.Text("Name", volume.Name),
            StatusItems.Text("Driver", volume.Driver),
            StatusItems.Text("Scope", volume.Scope),
            StatusItems.Text("Created", Format.Ago(volume.CreatedAt)),
            StatusItems.Text("Mount point", volume.Mountpoint),
            StatusItems.Text("Used by", usedBy.Count > 0 ? string.Join(", ", usedBy) : "No containers"),
        };
        if (volume.Labels?.GetValueOrDefault(ContainerExtensions.ComposeProjectLabel) is { } project)
        {
            metadata.Add(StatusItems.Text("Compose", project));
        }

        return new ListItem(specs[0].Command)
        {
            Title = title,
            Subtitle = subtitle,
            Icon = Icons.Volume,
            Tags = usedBy.Count > 0 ? [StatusItems.Colored("in use", 0x2E, 0xA0, 0x43)] : [],
            MoreCommands = CommandSpec.ToContextItems(specs.Skip(1)),
            Details = new Details { Title = title, Metadata = [.. metadata] },
        };
    }

    [GeneratedRegex("^[0-9a-f]+$")]
    private static partial Regex HexName();
}
