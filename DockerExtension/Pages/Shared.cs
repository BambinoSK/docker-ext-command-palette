using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DockerExtension.Commands;
using DockerExtension.Docker;
using DockerExtension.Helpers;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DockerExtension.Pages;

internal sealed partial class SimpleFilters : Filters
{
    private readonly IFilterItem[] _filters;

    public SimpleFilters(Action onChanged, params (string Id, string Name, IconInfo Icon)[] filters)
    {
        _filters = filters.Select(f => (IFilterItem)new Filter { Id = f.Id, Name = f.Name, Icon = f.Icon }).ToArray();
        CurrentFilterId = filters[0].Id;
        PropChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CurrentFilterId))
            {
                onChanged();
            }
        };
    }

    public override IFilterItem[] GetFilters() => _filters;
}

internal static class StatusItems
{
    public static IListItem[] EngineUnavailable(string error)
    {
        var more = new List<IContextItem> { new CommandContextItem(ResourceCommands.Retry()) };
        if (DockerDesktop.IsInstalled)
        {
            more.Insert(0, new CommandContextItem(DockerDesktop.StartCommand()));
        }

        more.Add(new CommandContextItem(SettingsManager.Instance.Settings.SettingsPage) { Title = "Extension settings", Icon = Icons.Settings });

        return
        [
            new ListItem(DockerDesktop.IsInstalled ? DockerDesktop.StartCommand() : ResourceCommands.Retry())
            {
                Title = "Docker engine is not reachable",
                Subtitle = error,
                Icon = Icons.Warning,
                MoreCommands = more.Skip(1).ToArray(),
                Details = new Details
                {
                    Title = "Docker engine is not reachable",
                    Body = $"{Format.Md(error)}\n\nEndpoint: `{DockerService.Instance.Endpoint.Host}` (from {DockerService.Instance.Endpoint.Source})\n\n" +
                           (DockerDesktop.IsInstalled
                               ? "Press **Enter** to start Docker Desktop. The list updates by itself once the engine is running."
                               : "Start your Docker engine, or set the Docker host in the extension settings."),
                },
            },
        ];
    }

    public static Tag StateTag(string state) => state switch
    {
        "running" => Colored(state, 0x2E, 0xA0, 0x43),
        "paused" => Colored(state, 0xC9, 0x8A, 0x00),
        "restarting" => Colored(state, 0x2F, 0x81, 0xF7),
        "dead" or "removing" => Colored(state, 0xD1, 0x34, 0x38),
        _ => new Tag(state),
    };

    public static Tag Colored(string text, byte r, byte g, byte b) => new(text) { Foreground = ColorHelpers.FromRgb(r, g, b) };

    public static DetailsElement Text(string key, string? value) =>
        new() { Key = key, Data = new DetailsLink { Text = string.IsNullOrEmpty(value) ? "—" : value } };

    public static DetailsElement Link(string key, string url, string text) =>
        new() { Key = key, Data = new DetailsLink(url, text) };

    public static DetailsElement Tags(string key, params Tag[] tags) =>
        new() { Key = key, Data = new DetailsTags { Tags = tags } };

    public static DetailsElement Separator() => new() { Key = string.Empty, Data = new DetailsSeparator() };
}

/// <summary>A list backed by an Engine API call that reloads when the matching resource changes.</summary>
internal abstract partial class ResourceListPage<T> : ListPage
{
    private readonly Lock _gate = new();
    private IReadOnlyList<T>? _data;
    private string? _error;
    private bool _stale = true;
    private bool _loading;

    protected ResourceListPage(DockerResource resource)
    {
        Resource = resource;
        DockerService.Instance.Changed += changed =>
        {
            if (changed == resource || changed == DockerResource.Engine)
            {
                _stale = true;
                RaiseItemsChanged();
            }
            else if (changed == DockerResource.Containers)
            {
                // Usage information comes from the container list.
                RaiseItemsChanged();
            }
        };
    }

    protected DockerResource Resource { get; }

    public override IListItem[] GetItems()
    {
        ShowDetails = SettingsManager.Instance.ShowDetails;
        DockerService.Instance.EnsureStarted();

        if (_stale)
        {
            StartLoad();
        }

        IReadOnlyList<T>? data;
        string? error;
        lock (_gate)
        {
            data = _data;
            error = _error;
        }

        if (error is not null)
        {
            return StatusItems.EngineUnavailable(error);
        }

        return data is null ? [] : BuildItems(data);
    }

    protected abstract Task<IReadOnlyList<T>> LoadAsync(DockerClient client, CancellationToken ct);

    protected abstract IListItem[] BuildItems(IReadOnlyList<T> data);

    private void StartLoad()
    {
        lock (_gate)
        {
            if (_loading)
            {
                return;
            }

            _loading = true;
            _stale = false;
        }

        IsLoading = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var data = await LoadAsync(DockerService.Instance.Client, CancellationToken.None).ConfigureAwait(false);
                lock (_gate)
                {
                    _data = data;
                    _error = null;
                }
            }
            catch (Exception ex) when (ex is DockerUnavailableException or DockerApiException or ObjectDisposedException)
            {
                lock (_gate)
                {
                    _data = null;
                    _error = ex.Message;
                }
            }
            finally
            {
                lock (_gate)
                {
                    _loading = false;
                }

                IsLoading = false;
                RaiseItemsChanged();
            }
        });
    }
}
