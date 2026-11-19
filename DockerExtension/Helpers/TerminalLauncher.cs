using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using DockerExtension.Docker;

namespace DockerExtension.Helpers;

/// <summary>Opens docker commands that need a visible console (exec, logs, run -it).</summary>
internal static class TerminalLauncher
{
    public static void RunDocker(string title, string[] dockerArgs, string? workingDirectory = null)
    {
        var args = DockerCli.GlobalArgs(DockerService.Instance.Endpoint).Concat(dockerArgs).ToList();
        var commandLine = Quote(DockerCli.Executable) + " " + string.Join(' ', args.Select(Quote));

        var settings = SettingsManager.Instance;
        var psi = settings.Terminal switch
        {
            SettingsManager.TerminalCustom => CustomTerminal(settings.CustomTerminalCommand, title, commandLine),
            SettingsManager.TerminalWindowsTerminal when WindowsTerminalPath() is { } wt =>
                new ProcessStartInfo(wt, $"new-tab --title {Quote(title)} {EscapeForWindowsTerminal(commandLine)}"),

            // /s makes cmd strip only the outer quotes, keeping the quoted docker.exe path intact.
            _ => new ProcessStartInfo("cmd.exe", $"/s /k \"title {title.Replace('"', '\'')} && {commandLine}\""),
        };

        psi.UseShellExecute = false;
        DockerCli.PrepareEnvironment(psi);
        if (!string.IsNullOrEmpty(workingDirectory) && Directory.Exists(workingDirectory))
        {
            psi.WorkingDirectory = workingDirectory;
        }

        Process.Start(psi)?.Dispose();
    }

    /// <summary>Opens an interactive terminal in a folder on this PC.</summary>
    public static void OpenFolder(string title, string directory)
    {
        var settings = SettingsManager.Instance;
        var psi = settings.Terminal switch
        {
            SettingsManager.TerminalCustom when settings.CustomFolderTerminalCommand.Length > 0 =>
                CustomFolderTerminal(settings.CustomFolderTerminalCommand, title, directory),
            SettingsManager.TerminalConsole => new ProcessStartInfo("cmd.exe", $"/k title {title.Replace('"', '\'')}"),

            // Windows Terminal opens new tabs in the profile's starting directory unless -d is given.
            _ when WindowsTerminalPath() is { } wt => new ProcessStartInfo(wt, $"new-tab -d {Quote(directory)} --title {Quote(title)}"),
            _ => new ProcessStartInfo("cmd.exe", $"/k title {title.Replace('"', '\'')}"),
        };

        psi.UseShellExecute = false;
        psi.WorkingDirectory = directory;
        DockerCli.PrepareEnvironment(psi);
        Process.Start(psi)?.Dispose();
    }

    internal static ProcessStartInfo CustomTerminal(string template, string title, string commandLine)
    {
        if (template.Length == 0)
        {
            throw new InvalidOperationException("Set the custom terminal command in the extension settings.");
        }

        var (program, arguments, isWindowsTerminal) = ResolveTemplate(template);
        if (isWindowsTerminal)
        {
            commandLine = EscapeForWindowsTerminal(commandLine);
        }

        arguments = arguments.Replace("{title}", Quote(title), StringComparison.Ordinal);
        arguments = arguments.Contains("{command}", StringComparison.Ordinal)
            ? arguments.Replace("{command}", Quote(commandLine), StringComparison.Ordinal)
            : $"{arguments} {commandLine}";

        return new ProcessStartInfo(program, arguments.Trim());
    }

    internal static ProcessStartInfo CustomFolderTerminal(string template, string title, string directory)
    {
        var (program, arguments, _) = ResolveTemplate(template);
        arguments = arguments
            .Replace("{title}", Quote(title), StringComparison.Ordinal)
            .Replace("{directory}", Quote(directory), StringComparison.Ordinal);
        return new ProcessStartInfo(program, arguments.Trim());
    }

    private static (string Program, string Arguments, bool IsWindowsTerminal) ResolveTemplate(string template)
    {
        var (program, arguments) = SplitProgram(template);
        var isWindowsTerminal = Path.GetFileNameWithoutExtension(program).Equals("wt", StringComparison.OrdinalIgnoreCase);
        return (isWindowsTerminal ? WindowsTerminalPath() ?? program : program, arguments, isWindowsTerminal);
    }

    private static (string Program, string Arguments) SplitProgram(string commandLine)
    {
        if (commandLine.StartsWith('"'))
        {
            var close = commandLine.IndexOf('"', 1);
            return close < 0
                ? (commandLine.Trim('"'), string.Empty)
                : (commandLine[1..close], commandLine[(close + 1)..].Trim());
        }

        var space = commandLine.IndexOfAny([' ', '\t']);
        return space < 0 ? (commandLine, string.Empty) : (commandLine[..space], commandLine[(space + 1)..].Trim());
    }

    private static string? WindowsTerminalPath()
    {
        var alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "wt.exe");
        return File.Exists(alias) ? alias : null;
    }

    // wt.exe treats an unescaped ';' as a separator between its own subcommands.
    private static string EscapeForWindowsTerminal(string commandLine) => commandLine.Replace(";", "\\;", StringComparison.Ordinal);

    // Windows command-line quoting as parsed by CommandLineToArgvW.
    internal static string Quote(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '"', '&', '|', '<', '>', '^']) < 0)
        {
            return arg;
        }

        var sb = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in arg)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                sb.Append('\\', (backslashes * 2) + 1);
            }
            else
            {
                sb.Append('\\', backslashes);
            }

            backslashes = 0;
            sb.Append(c);
        }

        sb.Append('\\', backslashes * 2);
        sb.Append('"');
        return sb.ToString();
    }
}
