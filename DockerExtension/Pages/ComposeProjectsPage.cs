using System.Collections.Concurrent;
using System.Linq;
using DockerExtension.Commands;
using DockerExtension.Docker;
using DockerExtension.Helpers;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DockerExtension.Pages;

internal sealed partial class ComposeProjectsPage : ListPage
{
    // One page per project: each page subscribes to engine changes, so they are reused rather than recreated per refresh.
    private static readonly ConcurrentDictionary<string, ContainersPage> ProjectPages = new();

    public ComposeProjectsPage()
    {
        Icon = Icons.Compose;
        Title = "Docker Compose projects";
        Name = "Open";
        PlaceholderText = "Search Compose projects";
        EmptyContent = new CommandItem(new NoOpCommand())
        {
            Title = "No Compose projects",
            Subtitle = "Projects appear here once they have containers (docker compose up)",
            Icon = Icons.Compose,
        };

        DockerService.Instance.Changed += changed =>
        {
            if (changed is DockerResource.Containers or DockerResource.Engine)
            {
                RaiseItemsChanged();
            }
        };
    }

    public static ContainersPage PageFor(string project) => ProjectPages.GetOrAdd(project, p => new ContainersPage(p));

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

        IsLoading = !service.HasLoadedContainers;
        return ComposeProject.FromContainers(service.Containers).Select(BuildItem).ToArray();
    }

    private static IListItem BuildItem(ComposeProject project)
    {
        var running = project.RunningCount;
        var total = project.Containers.Count;
        var state = running == total ? StatusItems.StateTag("running")
            : running == 0 ? new Tag("stopped")
            : StatusItems.Colored("partial", 0xC9, 0x8A, 0x00);

        var metadata = project.Containers
            .OrderBy(c => c.ComposeService())
            .Select(c => (IDetailsElement)StatusItems.Tags(c.ComposeService() ?? c.DisplayName(), StatusItems.StateTag(c.State)))
            .ToList();
        metadata.Add(StatusItems.Separator());
        metadata.Add(StatusItems.Text("Folder", project.WorkingDirectory));
        foreach (var file in project.ConfigFiles)
        {
            metadata.Add(StatusItems.Text("Compose file", file));
        }

        return new ListItem(PageFor(project.Name))
        {
            Title = project.Name,
            Subtitle = $"{running}/{total} running" + (project.WorkingDirectory is { } dir ? $" · {dir}" : string.Empty),
            Icon = Icons.Compose,
            Tags = [state],
            MoreCommands = CommandSpec.ToContextItems(ComposeCommands.For(project)),
            Details = new Details { Title = project.Name, Metadata = [.. metadata] },
        };
    }
}
