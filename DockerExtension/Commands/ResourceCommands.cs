using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DockerExtension.Docker;
using DockerExtension.Helpers;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DockerExtension.Commands;

internal static class ResourceCommands
{
    public static InvokableCommand Background(string title, IconInfo icon, string progress, string failure, DockerResource refresh, Func<DockerClient, CancellationToken, Task<string?>> work) =>
        new ActionCommand(title, icon, () => Toast.Run(() => work(DockerService.Instance.Client, CancellationToken.None), failure, refresh, progress));

    public static InvokableCommand Confirmed(InvokableCommand inner, string title, string description) => new ConfirmCommand(inner, title, description);

    public static InvokableCommand Terminal(string name, IconInfo icon, string title, string[] dockerArgs) =>
        new ActionCommand(name, icon, () =>
        {
            try
            {
                TerminalLauncher.RunDocker(title, dockerArgs);
                return CommandResult.Dismiss();
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                return Toast.Error($"Couldn't open a terminal: {ex.Message}");
            }
        });

    public static InvokableCommand Prune(string title, string what, string description, DockerResource refresh, Func<DockerClient, CancellationToken, Task<PruneResponse>> prune)
    {
        var inner = Background(title, Icons.Prune, $"Removing {what}…", $"Couldn't remove {what}", refresh, async (client, ct) =>
        {
            var result = await prune(client, ct).ConfigureAwait(false);
            return result.DeletedCount == 0 && result.SpaceReclaimed == 0
                ? $"No {what} to remove"
                : $"Removed {Format.Plural(result.DeletedCount, "item")}, reclaimed {Format.Bytes(result.SpaceReclaimed)}";
        });
        return new ConfirmCommand(inner, $"{title}?", description);
    }

    public static InvokableCommand Refresh(DockerResource resource) =>
        new ActionCommand("Refresh", Icons.Refresh, () =>
        {
            DockerService.Instance.RequestRefresh(resource);
            return CommandResult.KeepOpen();
        });

    public static InvokableCommand Retry() =>
        new ActionCommand("Retry", Icons.Refresh, () =>
        {
            DockerService.Instance.Reset();
            return CommandResult.KeepOpen();
        });
}

internal static class DockerDesktop
{
    public static string? ExecutablePath
    {
        get
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Docker", "Docker", "Docker Desktop.exe");
            return File.Exists(path) ? path : null;
        }
    }

    public static bool IsInstalled => ExecutablePath is not null;

    public static InvokableCommand StartCommand() =>
        new ActionCommand("Start Docker Desktop", Icons.Power, () =>
        {
            if (ExecutablePath is not { } exe)
            {
                return Toast.Error("Docker Desktop is not installed.");
            }

            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true })?.Dispose();
            DockerService.Instance.EnsureStarted();
            return Toast.Show("Starting Docker Desktop… the list fills in once the engine is up.", Icons.Power);
        });
}
