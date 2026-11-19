using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DockerExtension.Commands;
using DockerExtension.Docker;
using DockerExtension.Helpers;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Windows.System;

namespace DockerExtension.Pages;

internal sealed partial class NetworksPage : ResourceListPage<NetworkSummary>
{
    private static readonly HashSet<string> BuiltIn = new(StringComparer.Ordinal) { "bridge", "host", "none", "nat" };

    public NetworksPage()
        : base(DockerResource.Networks)
    {
        Icon = Icons.Network;
        Title = "Docker networks";
        Name = "Open";
        PlaceholderText = "Search networks";
        EmptyContent = new CommandItem(new NoOpCommand()) { Title = "No networks", Icon = Icons.Network };
    }

    protected override async Task<IReadOnlyList<NetworkSummary>> LoadAsync(DockerClient client, CancellationToken ct) =>
        await client.ListNetworksAsync(ct).ConfigureAwait(false);

    protected override IListItem[] BuildItems(IReadOnlyList<NetworkSummary> networks)
    {
        var usage = DockerService.Instance.Containers
            .SelectMany(c => c.NetworkNames().Select(n => (Network: n, Container: c.DisplayName())))
            .GroupBy(x => x.Network)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Container).ToList());

        var rows = networks
            .OrderBy(n => BuiltIn.Contains(n.Name) ? 1 : 0)
            .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .Select(n => (IListItem)BuildItem(n, usage.GetValueOrDefault(n.Name) ?? []))
            .ToList();

        var prune = ResourceCommands.Prune(
            "Remove unused networks",
            "unused networks",
            "Every custom network that no container uses is deleted.",
            DockerResource.Networks,
            (client, ct) => client.PruneNetworksAsync(ct));
        rows.Add(new ListItem(prune) { Title = prune.Name, Icon = Icons.Prune, Section = "Clean up" });
        return [.. rows];
    }

    private static ListItem BuildItem(NetworkSummary network, List<string> usedBy)
    {
        var builtIn = BuiltIn.Contains(network.Name);
        var subnet = network.IPAM?.Config?.FirstOrDefault(c => !string.IsNullOrEmpty(c.Subnet));

        var specs = new List<CommandSpec>
        {
            new(new CopyTextCommand(network.Name) { Name = "Copy name" }, "Copy name", Icons.Copy),
            new(new CopyTextCommand(network.Id) { Name = "Copy ID" }, "Copy ID", Icons.Copy, CommandSpec.CtrlShift(VirtualKey.C)),
        };

        if (!builtIn)
        {
            var remove = ResourceCommands.Background("Remove network", Icons.Delete, $"Removing {network.Name}…", $"Couldn't remove {network.Name}", DockerResource.Networks, async (client, ct) =>
            {
                await client.RemoveNetworkAsync(network.Id, ct).ConfigureAwait(false);
                return $"Removed {network.Name}";
            });
            specs.Add(new CommandSpec(
                ResourceCommands.Confirmed(remove, $"Remove {network.Name}?", "The network is deleted. Containers still connected to it block the removal."),
                "Remove network",
                Icons.Delete,
                CommandSpec.Ctrl(VirtualKey.Delete),
                Critical: true));
        }

        var subtitle = string.Join(" · ", new[] { network.Driver, network.Scope, subnet?.Subnet, usedBy.Count > 0 ? Format.Plural(usedBy.Count, "container") : null }
            .Where(s => !string.IsNullOrEmpty(s)));

        var tags = new List<ITag>();
        if (builtIn)
        {
            tags.Add(new Tag("built-in"));
        }

        if (network.Internal)
        {
            tags.Add(new Tag("internal"));
        }

        var metadata = new List<IDetailsElement>
        {
            StatusItems.Text("ID", Format.ShortId(network.Id)),
            StatusItems.Text("Driver", network.Driver),
            StatusItems.Text("Scope", network.Scope),
            StatusItems.Text("Subnet", subnet?.Subnet),
            StatusItems.Text("Gateway", subnet?.Gateway),
            StatusItems.Text("Created", Format.Ago(network.Created)),
            StatusItems.Text("Containers", usedBy.Count > 0 ? string.Join(", ", usedBy) : "None"),
        };

        return new ListItem(specs[0].Command)
        {
            Title = network.Name,
            Subtitle = subtitle,
            Icon = Icons.Network,
            Tags = [.. tags],
            MoreCommands = CommandSpec.ToContextItems(specs.Skip(1)),
            Details = new Details { Title = network.Name, Metadata = [.. metadata] },
        };
    }
}
