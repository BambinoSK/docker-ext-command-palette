using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using DockerExtension.Docker;
using DockerExtension.Helpers;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Windows.System;

namespace DockerExtension.Commands;

internal sealed record ComposeProject(string Name, string? WorkingDirectory, IReadOnlyList<string> ConfigFiles, IReadOnlyList<ContainerSummary> Containers)
{
    public int RunningCount => Containers.Count(c => c.IsRunning());

    public static IReadOnlyList<ComposeProject> FromContainers(IEnumerable<ContainerSummary> containers) =>
        containers
            .Where(c => c.ComposeProject() is not null)
            .GroupBy(c => c.ComposeProject()!, StringComparer.Ordinal)
            .Select(g =>
            {
                var first = g.First();
                var files = (first.Label(ContainerExtensions.ComposeConfigFilesLabel) ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return new ComposeProject(g.Key, first.Label(ContainerExtensions.ComposeWorkingDirLabel), files, g.ToList());
            })
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
}

internal static class ComposeCommands
{
    private static readonly TimeSpan LongTimeout = TimeSpan.FromMinutes(15);

    public static List<CommandSpec> For(ComposeProject project)
    {
        var running = project.RunningCount;
        var specs = new List<CommandSpec>
        {
            Cli(project, "Up", "Starting", "Started", Icons.Up, needsFiles: true, ["up", "-d"]) with { Shortcut = CommandSpec.Ctrl(VirtualKey.U) },
        };

        // Ctrl+S toggles: Start while anything is stopped, Stop once everything runs.
        if (running < project.Containers.Count)
        {
            specs.Add(Cli(project, "Start", "Starting", "Started", Icons.Start, needsFiles: false, ["start"]) with { Shortcut = CommandSpec.Ctrl(VirtualKey.S) });
        }

        if (running > 0)
        {
            var stop = Cli(project, "Stop", "Stopping", "Stopped", Icons.Stop, needsFiles: false, ["stop"]);
            specs.Add(running == project.Containers.Count ? stop with { Shortcut = CommandSpec.Ctrl(VirtualKey.S) } : stop);
        }

        specs.Add(Cli(project, "Restart", "Restarting", "Restarted", Icons.Restart, needsFiles: false, ["restart"]) with { Shortcut = CommandSpec.Ctrl(VirtualKey.R) });
        specs.Add(Cli(project, "Pull images", "Pulling images for", "Pulled images for", Icons.Pull, needsFiles: true, ["pull"]));
        specs.Add(new(
            new ActionCommand("View logs", Icons.Logs, () => Terminal(project, ["logs", "-f", "--tail", "200"])),
            "View logs",
            Icons.Logs,
            CommandSpec.Ctrl(VirtualKey.L)));

        if (project.WorkingDirectory is { } dir)
        {
            specs.Add(FolderTerminal(project.Name, dir));
            specs.Add(new CommandSpec(
                new ActionCommand("Open folder", Icons.Folder, () => Open("explorer.exe", dir)),
                "Open project folder",
                Icons.Folder,
                CommandSpec.Ctrl(VirtualKey.F)));
            specs.Add(new CommandSpec(
                new ActionCommand("Open in editor", Icons.Code, () => Open(SettingsManager.Instance.EditorCommand, dir)),
                "Open in editor",
                Icons.Code,
                CommandSpec.CtrlShift(VirtualKey.E)));
            specs.Add(new CommandSpec(new CopyTextCommand(dir) { Name = "Copy path" }, "Copy project path", Icons.Copy));
        }

        var down = Cli(project, "Down", "Removing", "Removed", Icons.Delete, needsFiles: false, ["down"]);
        specs.Add(down with
        {
            Command = new ConfirmCommand(
                (InvokableCommand)down.Command,
                $"Take down {project.Name}?",
                "Stops and removes the project's containers and networks. Volumes are kept."),
            Critical = true,
        });

        return specs;
    }

    public static CommandSpec FolderTerminal(string projectName, string directory) =>
        new(
            new ActionCommand("Open terminal", Icons.Shell, () =>
            {
                if (!Directory.Exists(directory))
                {
                    return Toast.Error($"Folder not found: {directory}");
                }

                try
                {
                    TerminalLauncher.OpenFolder(projectName, directory);
                    return CommandResult.Dismiss();
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    return Toast.Error($"Couldn't open a terminal: {ex.Message}");
                }
            }),
            "Open terminal in project folder",
            Icons.Shell,
            CommandSpec.Ctrl(VirtualKey.T));

    private static List<string> ProjectArgs(ComposeProject project, bool withFiles)
    {
        List<string> args = ["compose", "-p", project.Name];
        if (!withFiles)
        {
            return args;
        }

        var missing = project.ConfigFiles.Where(f => !File.Exists(f)).ToList();
        if (project.ConfigFiles.Count == 0 || missing.Count == project.ConfigFiles.Count)
        {
            throw new FileNotFoundException(project.ConfigFiles.Count == 0
                ? "The project's compose file location is unknown."
                : $"Compose file not found: {missing[0]}");
        }

        if (project.WorkingDirectory is { } dir)
        {
            args.Add("--project-directory");
            args.Add(dir);
        }

        foreach (var file in project.ConfigFiles.Except(missing))
        {
            args.Add("-f");
            args.Add(file);
        }

        return args;
    }

    private static CommandSpec Cli(ComposeProject project, string title, string progressVerb, string doneVerb, IconInfo icon, bool needsFiles, string[] verbArgs)
    {
        var command = new ActionCommand(title, icon, () =>
            Toast.Run(
                async () =>
                {
                    var args = ProjectArgs(project, needsFiles);
                    args.AddRange(verbArgs);
                    var result = await DockerCli.RunAsync([.. args], project.WorkingDirectory, LongTimeout, CancellationToken.None).ConfigureAwait(false);
                    if (!result.Succeeded)
                    {
                        throw new InvalidOperationException(result.Message);
                    }

                    return $"{doneVerb} {project.Name}";
                },
                $"docker compose {verbArgs[0]} failed for {project.Name}",
                DockerResource.Containers,
                $"{progressVerb} {project.Name}…"));

        return new CommandSpec(command, title, icon);
    }

    private static CommandResult Terminal(ComposeProject project, string[] verbArgs)
    {
        try
        {
            TerminalLauncher.RunDocker($"{project.Name} — logs", [.. ProjectArgs(project, withFiles: false), .. verbArgs], project.WorkingDirectory);
            return CommandResult.Dismiss();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return Toast.Error($"Couldn't open a terminal: {ex.Message}");
        }
    }

    private static CommandResult Open(string program, string directory)
    {
        if (!Directory.Exists(directory))
        {
            return Toast.Error($"Folder not found: {directory}");
        }

        return ShellHelpers.OpenInShell(program, TerminalLauncher.Quote(directory), directory, ShellHelpers.ShellRunAsType.None, program != "explorer.exe")
            ? CommandResult.Dismiss()
            : ShowFailure(program);
    }

    private static CommandResult ShowFailure(string program) =>
        Toast.Error($"Couldn't start '{program}'. Check the editor command in the extension settings.");
}
