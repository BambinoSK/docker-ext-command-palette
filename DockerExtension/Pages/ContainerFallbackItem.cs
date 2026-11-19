using System;
using System.Linq;
using DockerExtension.Commands;
using DockerExtension.Docker;
using DockerExtension.Helpers;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DockerExtension.Pages;

/// <summary>Offers the best-matching container for text typed in the main search box.</summary>
internal sealed partial class ContainerFallbackItem : FallbackCommandItem
{
    private const int MinQueryLength = 2;

    public ContainerFallbackItem()
        : base(new NoOpCommand(), "Find Docker containers", "docker.containers.fallback")
    {
        Title = string.Empty;
        Icon = Icons.Container;
    }

    // Called on every keystroke: reads only the cached container list.
    public override void UpdateQuery(string query)
    {
        if (!SettingsManager.Instance.FallbackEnabled || query is null || query.Trim().Length < MinQueryLength)
        {
            Clear();
            return;
        }

        var service = DockerService.Instance;
        service.EnsureStarted();

        var q = query.Trim();
        var match = service.Containers
            .Select(c => (Container: c, Name: c.DisplayName()))
            .Where(x => x.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(x => x.Container.IsRunning() ? 0 : 1)
            .ThenBy(x => x.Name.Length)
            .Select(x => x.Container)
            .FirstOrDefault();

        if (match is null)
        {
            Clear();
            return;
        }

        Command = new ContainerActionsPage(match);
        Title = match.DisplayName();
        Subtitle = $"Docker container · {match.State} · {match.Image}";
    }

    private void Clear()
    {
        Title = string.Empty;
        Subtitle = string.Empty;
        Command = new NoOpCommand();
    }
}
