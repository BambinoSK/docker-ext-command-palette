using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DockerExtension.Docker;
using DockerExtension.Helpers;
using DockerExtension.Pages;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Windows.System;

namespace DockerExtension.Commands;

internal static class ContainerCommands
{
    /// <summary>Enter on a container: stop it while it runs, start it otherwise.</summary>
    public static CommandSpec Primary(ContainerSummary c) =>
        c.IsRunning() || c.IsPaused()
            ? Operation(c, "Stop", "Stopping", "Stopped", "stop", Icons.Stop, (client, id, ct) => client.StopContainerAsync(id, ct))
            : Operation(c, "Start", "Starting", "Started", "start", Icons.Start, (client, id, ct) => StartAndConfirmAsync(client, id, client.StartContainerAsync, ct));

    public static List<CommandSpec> Secondary(ContainerSummary c, bool includeComposeLink = true)
    {
        var name = c.DisplayName();
        var running = c.IsRunning();
        var specs = new List<CommandSpec>();

        if (running)
        {
            specs.Add(new CommandSpec(
                new ActionCommand("Open shell", Icons.Shell, () => Launch($"{name} — shell", ["exec", "-it", c.Id, .. ShellCommand()])),
                "Open shell",
                Icons.Shell,
                CommandSpec.Ctrl(VirtualKey.E)));
        }

        specs.Add(new CommandSpec(
            new ActionCommand("View logs", Icons.Logs, () => Launch($"{name} — logs", ["logs", "-f", "--tail", "200", c.Id])),
            "View logs",
            Icons.Logs,
            CommandSpec.Ctrl(VirtualKey.L)));

        if (c.ComposeProject() is { } composeProject && c.Label(ContainerExtensions.ComposeWorkingDirLabel) is { Length: > 0 } projectDir)
        {
            specs.Add(ComposeCommands.FolderTerminal(composeProject, projectDir));
        }

        if (running)
        {
            var first = true;
            foreach (var (hostPort, containerPort, url) in c.WebPorts())
            {
                specs.Add(new CommandSpec(
                    new OpenUrlCommand(url) { Name = $"Open {url}" },
                    $"Open localhost:{hostPort} (→ {containerPort})",
                    Icons.Browser,
                    first ? CommandSpec.Ctrl(VirtualKey.O) : null));
                first = false;
            }

            specs.Add(Operation(c, "Restart", "Restarting", "Restarted", "restart", Icons.Restart, (client, id, ct) => StartAndConfirmAsync(client, id, client.RestartContainerAsync, ct)) with { Shortcut = CommandSpec.Ctrl(VirtualKey.R) });
        }

        if (c.State == "running")
        {
            specs.Add(Operation(c, "Pause", "Pausing", "Paused", "pause", Icons.Pause, (client, id, ct) => client.PauseContainerAsync(id, ct)) with { Shortcut = CommandSpec.Ctrl(VirtualKey.P) });
        }
        else if (c.IsPaused())
        {
            specs.Add(Operation(c, "Resume", "Resuming", "Resumed", "resume", Icons.Start, (client, id, ct) => client.UnpauseContainerAsync(id, ct)) with { Shortcut = CommandSpec.Ctrl(VirtualKey.P) });
        }

        specs.Add(new CommandSpec(new ContainerInspectPage(c.Id, name), "Inspect", Icons.Info, CommandSpec.Ctrl(VirtualKey.I)));

        if (includeComposeLink && c.ComposeProject() is { } project)
        {
            specs.Add(new CommandSpec(ComposeProjectsPage.PageFor(project), $"Compose project: {project}", Icons.Compose));
        }

        specs.Add(new CommandSpec(new CopyTextCommand(name) { Name = "Copy name" }, "Copy name", Icons.Copy));
        specs.Add(new CommandSpec(new CopyTextCommand(c.Id) { Name = "Copy ID" }, "Copy ID", Icons.Copy, CommandSpec.CtrlShift(VirtualKey.C)));
        if (c.IpAddress() is { } ip)
        {
            specs.Add(new CommandSpec(new CopyTextCommand(ip) { Name = "Copy IP address" }, $"Copy IP address ({ip})", Icons.Copy));
        }

        if (running)
        {
            var kill = Operation(c, "Kill", "Killing", "Killed", "kill", Icons.Kill, (client, id, ct) => client.KillContainerAsync(id, ct));
            specs.Add(kill with
            {
                Command = new ConfirmCommand((InvokableCommand)kill.Command, $"Kill {name}?", "The container's processes are stopped immediately without a graceful shutdown."),
                Critical = true,
            });
        }

        var remove = Operation(c, "Remove", "Removing", "Removed", "remove", Icons.Delete, (client, id, ct) => client.RemoveContainerAsync(id, force: true, ct));
        specs.Add(remove with
        {
            Command = new ConfirmCommand(
                (InvokableCommand)remove.Command,
                $"Remove {name}?",
                running ? "The container is running and will be stopped and deleted. Named volumes are kept." : "The container is deleted. Named volumes are kept."),
            Shortcut = CommandSpec.Ctrl(VirtualKey.Delete),
            Critical = true,
        });

        return specs;
    }

    private static string[] ShellCommand()
    {
        var custom = SettingsManager.Instance.ContainerShell;
        if (custom.Length > 0)
        {
            return custom.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }

        return DockerService.Instance.EngineOsType == "windows"
            ? ["cmd"]
            : ["sh", "-c", "command -v bash >/dev/null && exec bash || exec sh"];
    }

    // The engine reports success as soon as the process is launched, even if it exits immediately afterwards.
    private static async Task StartAndConfirmAsync(DockerClient client, string id, Func<string, CancellationToken, Task> start, CancellationToken ct)
    {
        await start(id, ct).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromSeconds(1.5), ct).ConfigureAwait(false);

        var state = (await client.InspectContainerAsync(id, ct).ConfigureAwait(false)).State;
        if (state is { Running: false, Restarting: false })
        {
            throw new InvalidOperationException($"it exited right after starting (exit code {state.ExitCode}). View its logs to see why");
        }
    }

    private static CommandResult Launch(string title, string[] args)
    {
        try
        {
            TerminalLauncher.RunDocker(title, args);
            return CommandResult.Dismiss();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return Toast.Error($"Couldn't open a terminal: {ex.Message}");
        }
    }

    private static CommandSpec Operation(
        ContainerSummary c,
        string title,
        string progressVerb,
        string doneVerb,
        string failVerb,
        IconInfo icon,
        Func<DockerClient, string, CancellationToken, Task> operation)
    {
        var name = c.DisplayName();
        var id = c.Id;
        var command = new ActionCommand(title, icon, () =>
            Toast.Run(
                async () =>
                {
                    await operation(DockerService.Instance.Client, id, CancellationToken.None).ConfigureAwait(false);
                    return $"{doneVerb} {name}";
                },
                $"Couldn't {failVerb} {name}",
                DockerResource.Containers,
                $"{progressVerb} {name}…"));

        return new CommandSpec(command, title, icon);
    }
}

internal sealed partial class ContainerActionsPage : ListPage
{
    private readonly ContainerSummary _container;

    public ContainerActionsPage(ContainerSummary container)
    {
        _container = container;
        Name = "Show actions";
        Title = container.DisplayName();
        Icon = Icons.Container;
        PlaceholderText = $"Actions for {container.DisplayName()}";
    }

    public override IListItem[] GetItems()
    {
        var primary = ContainerCommands.Primary(_container);
        return [primary.ToListItem(), .. ContainerCommands.Secondary(_container).Select(s => s.ToListItem())];
    }
}
