using System;
using System.Collections.Generic;
using System.Linq;
using DockerExtension.Commands;
using DockerExtension.Docker;
using DockerExtension.Helpers;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DockerExtension.Pages;

/// <summary>All containers grouped by Compose project, or the containers of one project.</summary>
internal sealed partial class ContainersPage : ListPage
{
    private const string FilterAll = "all";
    private const string FilterRunning = "running";
    private const string FilterStopped = "stopped";
    private const string StandaloneSection = "Containers";

    private readonly string? _project;
    private readonly SimpleFilters _filters;

    public ContainersPage(string? project = null)
    {
        _project = project;
        Icon = project is null ? Icons.Container : Icons.Compose;
        Title = project is null ? "Docker containers" : $"Compose: {project}";
        Name = "Open";
        PlaceholderText = "Search containers by name, image or port";
        EmptyContent = new CommandItem(new NoOpCommand())
        {
            Title = "No containers",
            Subtitle = "Nothing matches this filter",
            Icon = Icons.Container,
        };

        _filters = new SimpleFilters(
            () => RaiseItemsChanged(),
            (FilterAll, "All", Icons.All),
            (FilterRunning, "Running", Icons.Start),
            (FilterStopped, "Stopped", Icons.Stop));
        Filters = _filters;

        DockerService.Instance.Changed += changed =>
        {
            if (changed is DockerResource.Containers or DockerResource.Engine)
            {
                RaiseItemsChanged();
            }
        };
    }

    public override IListItem[] GetItems()
    {
        ShowDetails = SettingsManager.Instance.ShowDetails;
        var service = DockerService.Instance;
        service.EnsureStarted();

        if (service.LastError is { } error)
        {
            IsLoading = false;
            return StatusItems.EngineUnavailable(error);
        }

        if (!service.HasLoadedContainers)
        {
            IsLoading = true;
            return [];
        }

        IsLoading = false;

        IEnumerable<ContainerSummary> containers = service.Containers;
        if (_project is not null)
        {
            containers = containers.Where(c => c.ComposeProject() == _project);
        }

        containers = _filters.CurrentFilterId switch
        {
            FilterRunning => containers.Where(c => c.IsRunning() || c.IsPaused()),
            FilterStopped => containers.Where(c => !c.IsRunning() && !c.IsPaused()),
            _ => containers,
        };

        var items = containers
            .Select(c => (Container: c, Section: SectionFor(c)))
            .OrderBy(x => x.Section == StandaloneSection ? 0 : 1)
            .ThenBy(x => x.Section, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Container.IsRunning() ? 0 : 1)
            .ThenBy(x => x.Container.DisplayName(), StringComparer.OrdinalIgnoreCase)
            .Select(x => (IListItem)BuildItem(x.Container, x.Section))
            .ToList();

        if (_project is not null)
        {
            var project = ComposeProject.FromContainers(service.Containers).FirstOrDefault(p => p.Name == _project);
            if (project is not null)
            {
                items.AddRange(ComposeCommands.For(project).Select(spec =>
                {
                    var item = spec.ToListItem();
                    item.Section = "Project";
                    return (IListItem)item;
                }));
            }
        }

        return [.. items];
    }

    private string? SectionFor(ContainerSummary c)
    {
        if (_project is not null)
        {
            return null;
        }

        return c.ComposeProject() is { } project ? $"Compose · {project}" : StandaloneSection;
    }

    private ListItem BuildItem(ContainerSummary c, string? section)
    {
        var primary = ContainerCommands.Primary(c);
        var name = c.DisplayName();
        var ports = c.PortsSummary();
        var subtitle = string.Join(" · ", new[] { c.Image, c.Status, ports }.Where(s => !string.IsNullOrEmpty(s)));

        var tags = new List<ITag> { StatusItems.StateTag(c.State) };
        switch (c.Health())
        {
            case "unhealthy":
                tags.Add(StatusItems.Colored("unhealthy", 0xD1, 0x34, 0x38));
                break;
            case "starting":
                tags.Add(new Tag("starting"));
                break;
        }

        var item = new ListItem(primary.Command)
        {
            Title = name,
            Subtitle = subtitle,
            Icon = Icons.Container,
            Tags = [.. tags],
            MoreCommands = CommandSpec.ToContextItems(ContainerCommands.Secondary(c, includeComposeLink: _project is null)),
            Details = BuildDetails(c),
        };

        if (section is not null)
        {
            item.Section = section;
        }

        return item;
    }

    private static Details BuildDetails(ContainerSummary c)
    {
        var metadata = new List<IDetailsElement>
        {
            StatusItems.Tags("State", StatusItems.StateTag(c.State)),
            StatusItems.Text("Status", c.Status),
            StatusItems.Text("Image", c.Image),
            StatusItems.Text("ID", Format.ShortId(c.Id)),
            StatusItems.Text("Created", Format.Ago(c.Created)),
        };

        if (c.ComposeProject() is { } project)
        {
            metadata.Add(StatusItems.Text("Compose", $"{project} / {c.ComposeService()}"));
        }

        foreach (var (hostPort, containerPort, url) in c.WebPorts())
        {
            metadata.Add(c.IsRunning()
                ? StatusItems.Link("Port", url, $"localhost:{hostPort} → {containerPort}")
                : StatusItems.Text("Port", $"{hostPort} → {containerPort}"));
        }

        var networks = string.Join(", ", c.NetworkNames());
        if (networks.Length > 0)
        {
            metadata.Add(StatusItems.Text("Networks", networks));
        }

        if (c.IpAddress() is { } ip)
        {
            metadata.Add(StatusItems.Text("IP address", ip));
        }

        if (c.Mounts is { Count: > 0 } mounts)
        {
            metadata.Add(StatusItems.Separator());
            foreach (var m in mounts.Take(8))
            {
                var source = m.Type == "volume" ? m.Name : m.Source;
                metadata.Add(StatusItems.Text(m.Type == "volume" ? "Volume" : "Mount", $"{source} → {m.Destination}"));
            }
        }

        return new Details
        {
            Title = c.DisplayName(),
            Body = string.IsNullOrEmpty(c.Command) ? string.Empty : $"`{c.Command.Replace("`", "'", StringComparison.Ordinal)}`",
            Metadata = [.. metadata],
        };
    }
}
