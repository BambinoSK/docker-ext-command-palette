using System;
using System.Threading.Tasks;
using DockerExtension.Docker;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DockerExtension.Helpers;

// Command Palette only pops up a toast returned as a command result; status messages
// (ExtensionHost.ShowStatus, ToastStatusMessage) go to its status area instead.
internal static class Toast
{
    // Work that takes longer continues in the background and reports through the status area.
    private static readonly TimeSpan QuickWait = TimeSpan.FromSeconds(4);

    private const int BackgroundErrorDurationMs = 10_000;

    public static CommandResult Show(string message, IconInfo? icon = null) =>
        CommandResult.ShowToast(new ToastArgs { Message = message, Icon = icon, Result = CommandResult.KeepOpen() });

    public static CommandResult Error(string message) => Show(message, Icons.Failure);

    /// <summary>
    /// Runs engine work and returns its outcome as a toast when it finishes within a few seconds;
    /// otherwise shows progress in the status area and reports the outcome there.
    /// </summary>
    public static CommandResult Run(Func<Task<string?>> work, string failurePrefix, DockerResource refresh, string progressMessage)
    {
        var task = Task.Run(work);

        // Command Palette invokes commands on a background thread, so this wait does not block its UI.
        if (Task.WhenAny(task, Task.Delay(QuickWait)).GetAwaiter().GetResult() == task)
        {
            DockerService.Instance.RequestRefresh(refresh);
            var (message, succeeded) = Outcome(task, failurePrefix);
            return message is null ? CommandResult.KeepOpen() : Show(message, succeeded ? Icons.Success : Icons.Failure);
        }

        var progress = new StatusMessage
        {
            Message = progressMessage,
            State = MessageState.Info,
            Progress = new ProgressState { IsIndeterminate = true },
        };
        ExtensionHost.ShowStatus(progress, StatusContext.Extension);

        _ = task.ContinueWith(
            finished =>
            {
                ExtensionHost.HideStatus(progress);
                DockerService.Instance.RequestRefresh(refresh);
                var (message, succeeded) = Outcome(finished, failurePrefix);
                if (message is not null)
                {
                    var status = new StatusMessage { Message = message, State = succeeded ? MessageState.Success : MessageState.Error };
                    if (succeeded)
                    {
                        new ToastStatusMessage(status).Show();
                    }
                    else
                    {
                        new ToastStatusMessage(status) { Duration = BackgroundErrorDurationMs }.Show();
                    }
                }
            },
            TaskScheduler.Default);

        return Show($"{progressMessage} It continues in the background.", Icons.Info);
    }

    private static (string? Message, bool Succeeded) Outcome(Task<string?> task, string failurePrefix) =>
        task.IsCompletedSuccessfully
            ? (task.Result, true)
            : ($"{failurePrefix}: {task.Exception?.GetBaseException().Message ?? "cancelled"}", false);
}
