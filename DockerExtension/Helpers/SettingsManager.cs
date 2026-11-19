// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.IO;
using DockerExtension.Docker;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DockerExtension;

internal sealed partial class SettingsManager : JsonSettingsManager
{
    public const string TerminalWindowsTerminal = "wt";
    public const string TerminalConsole = "cmd";
    public const string TerminalCustom = "custom";

    private static readonly string _namespace = "DockerExtension";

    private readonly TextSetting _endpoint = new(
        Namespaced(nameof(EndpointOverride)),
        "Docker host",
        "Leave empty to follow DOCKER_HOST and the current docker context. Examples: npipe:////./pipe/docker_engine, tcp://localhost:2375",
        string.Empty)
    {
        Placeholder = "Automatic",
    };

    private readonly ToggleSetting _confirmDestructive = new(
        Namespaced(nameof(ConfirmDestructive)),
        "Confirm destructive actions",
        "Ask before removing containers, images, volumes and networks, and before pruning",
        true);

    private readonly ChoiceSetSetting _terminal = new(
        Namespaced(nameof(Terminal)),
        "Terminal",
        "Where shells, logs and interactive runs open",
        [
            new ChoiceSetSetting.Choice("Windows Terminal", TerminalWindowsTerminal),
            new ChoiceSetSetting.Choice("Console window (cmd)", TerminalConsole),
            new ChoiceSetSetting.Choice("Custom command", TerminalCustom),
        ]);

    private readonly TextSetting _customTerminal = new(
        Namespaced(nameof(CustomTerminalCommand)),
        "Custom terminal command",
        "Used when Terminal is 'Custom command'. {command} is the docker command as one quoted argument, {title} the window title. Without {command}, the docker program and its arguments are appended",
        string.Empty)
    {
        Placeholder = """wt.exe new-tab -p "Git Bash" --title {title} "C:\Program Files\Git\bin\bash.exe" -lc {command}""",
    };

    private readonly TextSetting _customFolderTerminal = new(
        Namespaced(nameof(CustomFolderTerminalCommand)),
        "Custom terminal command for folders",
        "Used by 'Open terminal in project folder' when Terminal is 'Custom command'. {directory} is the folder, {title} the window title. Leave empty to use Windows Terminal",
        string.Empty)
    {
        Placeholder = """wt.exe new-tab -p "Git Bash" -d {directory} --title {title}""",
    };

    private readonly TextSetting _shell = new(
        Namespaced(nameof(ContainerShell)),
        "Container shell",
        "Command started by 'Open shell'. Leave empty to use bash when available, otherwise sh (cmd for Windows containers)",
        string.Empty)
    {
        Placeholder = "Automatic",
    };

    private readonly TextSetting _editor = new(
        Namespaced(nameof(EditorCommand)),
        "Editor command",
        "Command used by 'Open in editor' for Compose project folders",
        "code");

    private readonly ToggleSetting _fallback = new(
        Namespaced(nameof(FallbackEnabled)),
        "Find containers from the main search",
        "Show a matching container when you type its name in Command Palette's main search box",
        true);

    private readonly ToggleSetting _showDetails = new(
        Namespaced(nameof(ShowDetails)),
        "Show details panel",
        "Open the details panel next to lists",
        true);

    private string _lastEndpoint;

    private SettingsManager()
    {
        FilePath = SettingsJsonPath();

        Settings.Add(_endpoint);
        Settings.Add(_terminal);
        Settings.Add(_customTerminal);
        Settings.Add(_customFolderTerminal);
        Settings.Add(_shell);
        Settings.Add(_editor);
        Settings.Add(_confirmDestructive);
        Settings.Add(_fallback);
        Settings.Add(_showDetails);

        LoadSettings();
        _lastEndpoint = EndpointOverride;

        Settings.SettingsChanged += (_, _) =>
        {
            SaveSettings();
            if (EndpointOverride != _lastEndpoint)
            {
                _lastEndpoint = EndpointOverride;
                DockerService.Instance.Reset();
            }
        };
    }

    public static SettingsManager Instance { get; } = new();

    public string EndpointOverride => (_endpoint.Value ?? string.Empty).Trim();

    public bool ConfirmDestructive => _confirmDestructive.Value;

    public string Terminal => string.IsNullOrEmpty(_terminal.Value) ? TerminalWindowsTerminal : _terminal.Value;

    public string CustomTerminalCommand => (_customTerminal.Value ?? string.Empty).Trim();

    public string CustomFolderTerminalCommand => (_customFolderTerminal.Value ?? string.Empty).Trim();

    public string ContainerShell => (_shell.Value ?? string.Empty).Trim();

    public string EditorCommand => string.IsNullOrWhiteSpace(_editor.Value) ? "code" : _editor.Value.Trim();

    public bool FallbackEnabled => _fallback.Value;

    public bool ShowDetails => _showDetails.Value;

    internal static string SettingsJsonPath()
    {
        var directory = Utilities.BaseSettingsPath(ExtensionIdentity.StorageName);
        Directory.CreateDirectory(directory);

        return Path.Combine(directory, "settings.json");
    }

    private static string Namespaced(string propertyName) => $"{_namespace}.{propertyName}";
}
