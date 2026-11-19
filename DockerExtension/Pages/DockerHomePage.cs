using System.Linq;
using DockerExtension.Commands;
using DockerExtension.Docker;
using DockerExtension.Helpers;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DockerExtension.Pages;

internal sealed partial class DockerHomePage : ListPage
{
    private readonly ListItem _containers;
    private readonly ListItem _compose;
    private readonly ListItem[] _sections;

    public DockerHomePage(ICommand settingsPage)
    {
        Id = "docker.home";
        Icon = Icons.App;
        Title = ExtensionIdentity.DisplayName;
        Name = "Open";
        PlaceholderText = "Search Docker";

        var settings = new CommandContextItem(settingsPage) { Title = "Extension settings", Icon = Icons.Settings };
        _containers = Section(new ContainersPage { Id = "docker.containers" }, "Containers", Icons.Container, settings);
        _compose = Section(new ComposeProjectsPage { Id = "docker.compose" }, "Compose projects", Icons.Compose, settings);
        _sections =
        [
            _containers,
            _compose,
            Section(new ImagesPage { Id = "docker.images" }, "Images", Icons.Image, settings, "Run, pull and remove images"),
            Section(new VolumesPage { Id = "docker.volumes" }, "Volumes", Icons.Volume, settings, "Browse and remove volumes"),
            Section(new NetworksPage { Id = "docker.networks" }, "Networks", Icons.Network, settings, "Browse and remove networks"),
            Section(new SystemPage { Id = "docker.system" }, "System", Icons.Engine, settings, "Engine status, contexts, disk usage and clean-up"),
        ];
        UpdateSubtitles();

        DockerService.Instance.Changed += changed =>
        {
            if (changed is DockerResource.Containers or DockerResource.Engine)
            {
                UpdateSubtitles();
                RaiseItemsChanged();
            }
        };
    }

    // Command Palette restores pinned sections by their page Id. The same items are returned
    // so a pinned section keeps its live subtitle.
    public ICommandItem? SectionFor(string id) => _sections.FirstOrDefault(s => s.Command?.Id == id);

    public override IListItem[] GetItems()
    {
        var service = DockerService.Instance;
        service.EnsureStarted();

        return service.LastError is { } error
            ? [.. StatusItems.EngineUnavailable(error), .. _sections]
            : _sections;
    }

    private static ListItem Section(ICommand page, string title, IconInfo icon, CommandContextItem settings, string subtitle = "") =>
        new(page) { Title = title, Subtitle = subtitle, Icon = icon, MoreCommands = [settings] };

    private void UpdateSubtitles()
    {
        var service = DockerService.Instance;
        if (!service.HasLoadedContainers)
        {
            _containers.Subtitle = "Start, stop, open a shell, view logs";
            _compose.Subtitle = "Up, down and restart Compose projects";
            return;
        }

        var running = service.Containers.Count(c => c.IsRunning());
        _containers.Subtitle = $"{running} running · {Format.Plural(service.Containers.Count, "container")}";
        _compose.Subtitle = Format.Plural(ComposeProject.FromContainers(service.Containers).Count, "project");
    }
}
