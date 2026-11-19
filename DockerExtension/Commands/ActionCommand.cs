using System;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DockerExtension.Commands;

internal sealed partial class ActionCommand : InvokableCommand
{
    private readonly Func<CommandResult> _action;

    public ActionCommand(string name, IconInfo icon, Func<CommandResult> action)
    {
        Name = name;
        Icon = icon;
        _action = action;
    }

    public override ICommandResult Invoke() => _action();
}

/// <summary>Asks before running <paramref name="inner"/> while "Confirm destructive actions" is on.</summary>
internal sealed partial class ConfirmCommand : InvokableCommand
{
    private readonly InvokableCommand _inner;
    private readonly string _title;
    private readonly string _description;

    public ConfirmCommand(InvokableCommand inner, string title, string description)
    {
        _inner = inner;
        _title = title;
        _description = description;
        Name = inner.Name;
        Icon = inner.Icon;
    }

    public override ICommandResult Invoke()
    {
        if (!SettingsManager.Instance.ConfirmDestructive)
        {
            return _inner.Invoke();
        }

        return CommandResult.Confirm(new ConfirmationArgs
        {
            Title = _title,
            Description = _description,
            PrimaryCommand = _inner,
            IsPrimaryCommandCritical = true,
        });
    }
}
