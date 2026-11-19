using System.Collections.Generic;
using System.Linq;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Windows.System;

namespace DockerExtension.Commands;

/// <summary>One action on a resource, renderable as a context-menu entry or as a list row.</summary>
internal sealed record CommandSpec(ICommand Command, string Title, IconInfo Icon, KeyChord? Shortcut = null, bool Critical = false)
{
    public static KeyChord Ctrl(VirtualKey key) => KeyChordHelpers.FromModifiers(true, false, false, false, key, 0);

    public static KeyChord CtrlShift(VirtualKey key) => KeyChordHelpers.FromModifiers(true, false, true, false, key, 0);

    public CommandContextItem ToContextItem()
    {
        var item = new CommandContextItem(Command) { Title = Title, Icon = Icon, IsCritical = Critical };
        if (Shortcut is { } chord)
        {
            item.RequestedShortcut = chord;
        }

        return item;
    }

    public ListItem ToListItem() => new(Command) { Title = Title, Icon = Icon };

    public static IContextItem[] ToContextItems(IEnumerable<CommandSpec> specs) =>
        specs.Select(s => (IContextItem)s.ToContextItem()).ToArray();
}
