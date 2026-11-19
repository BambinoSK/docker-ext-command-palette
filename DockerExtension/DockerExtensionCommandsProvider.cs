// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using DockerExtension.Docker;
using DockerExtension.Helpers;
using DockerExtension.Pages;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DockerExtension;

public partial class DockerExtensionCommandsProvider : CommandProvider
{
    private readonly DockerHomePage _home;
    private readonly ICommandItem[] _commands;
    private readonly IFallbackCommandItem[] _fallbacks;

    public DockerExtensionCommandsProvider()
    {
        Id = ExtensionIdentity.ProviderId;
        DisplayName = ExtensionIdentity.DisplayName;
        Icon = Icons.App;

        var settings = SettingsManager.Instance.Settings;
        _home = new DockerHomePage(settings.SettingsPage);

        _commands =
        [
            new CommandItem(_home)
            {
                Title = ExtensionIdentity.DisplayName,
                Subtitle = "Containers, Compose, images, volumes, networks and system",
                Icon = Icons.App,
                MoreCommands = [new CommandContextItem(settings.SettingsPage) { Title = "Extension settings", Icon = Icons.Settings }],
            },
        ];

        _fallbacks = [new ContainerFallbackItem()];

        Settings = settings;
    }

    public override ICommandItem[] TopLevelCommands() => _commands;

    public override IFallbackCommandItem[] FallbackCommands() => _fallbacks;

    public override ICommandItem? GetCommandItem(string id) => _home.SectionFor(id);

    public override void Dispose()
    {
        DockerService.Instance.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
