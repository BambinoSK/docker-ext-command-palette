using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DockerExtension.Commands;
using DockerExtension.Docker;
using DockerExtension.Helpers;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DockerExtension.Pages;

internal sealed partial class ContainerInspectPage : ContentPage
{
    private readonly string _id;
    private readonly MarkdownContent _content = new();
    private ContainerInspect? _inspect;
    private string? _usage;
    private int _loadStarted;

    public ContainerInspectPage(string id, string name)
    {
        _id = id;
        Name = "Inspect";
        Title = name;
        Icon = Icons.Info;
        Commands =
        [
            new CommandContextItem(new ActionCommand("Refresh", Icons.Refresh, () =>
            {
                Interlocked.Exchange(ref _loadStarted, 0);
                RaiseItemsChanged();
                return CommandResult.KeepOpen();
            })),
        ];
    }

    public override IContent[] GetContent()
    {
        if (Interlocked.Exchange(ref _loadStarted, 1) == 0)
        {
            _ = LoadAsync();
        }

        _content.Body = _inspect is null ? "Loading…" : Render(_inspect, _usage);
        return [_content];
    }

    private async Task LoadAsync()
    {
        IsLoading = true;
        var client = DockerService.Instance.Client;
        try
        {
            _inspect = await client.InspectContainerAsync(_id, CancellationToken.None).ConfigureAwait(false);
            _usage = null;
            _content.Body = Render(_inspect, null);

            if (_inspect.State?.Running == true)
            {
                var stats = await client.GetContainerStatsAsync(_id, CancellationToken.None).ConfigureAwait(false);
                _usage = DescribeUsage(stats);
                _content.Body = Render(_inspect, _usage);
            }
        }
        catch (Exception ex) when (ex is DockerUnavailableException or DockerApiException or ObjectDisposedException)
        {
            _content.Body = $"**Couldn't inspect the container.**\n\n{Format.Md(ex.Message)}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string DescribeUsage(ContainerStats stats)
    {
        var cpu = stats.CpuStats;
        var pre = stats.PreCpuStats;
        var cpuText = "—";
        if (cpu?.CpuUsage is { } usage && pre?.CpuUsage is { } preUsage && cpu.SystemCpuUsage > pre.SystemCpuUsage)
        {
            var cpus = cpu.OnlineCpus > 0 ? cpu.OnlineCpus : (uint)(usage.PerCpuUsage?.Count ?? 1);
            var percent = (double)(usage.TotalUsage - preUsage.TotalUsage) / (cpu.SystemCpuUsage - pre.SystemCpuUsage) * cpus * 100;
            cpuText = $"{percent:0.0} %";
        }

        var mem = stats.MemoryStats;
        var memText = "—";
        if (mem is not null)
        {
            if (mem.Usage > 0)
            {
                // Matches `docker stats`: page cache that can be reclaimed is not counted.
                var cache = mem.Stats?.GetValueOrDefault("inactive_file") ?? mem.Stats?.GetValueOrDefault("total_inactive_file") ?? 0;
                var used = mem.Usage > cache ? mem.Usage - cache : mem.Usage;
                memText = mem.Limit > 0 ? $"{Format.Bytes((long)used)} / {Format.Bytes((long)mem.Limit)}" : Format.Bytes((long)used);
            }
            else if (mem.PrivateWorkingSet > 0)
            {
                memText = Format.Bytes((long)mem.PrivateWorkingSet);
            }
        }

        return $"| CPU | {cpuText} |\n| Memory | {memText} |\n| Processes | {stats.NumProcs} |\n";
    }

    private static string Render(ContainerInspect c, string? usage)
    {
        var sb = new StringBuilder();
        var state = c.State;
        var status = state?.Status ?? "unknown";
        if (state?.Health?.Status is { Length: > 0 } health)
        {
            status += $" ({health})";
        }

        sb.Append("# ").AppendLine(Format.Md(c.Name.TrimStart('/'))).AppendLine();
        sb.AppendLine("| | |").AppendLine("|---|---|");
        Row(sb, "Status", status);
        if (state is { Running: false, ExitCode: not 0 })
        {
            Row(sb, "Exit code", state.ExitCode.ToString(CultureInfo.InvariantCulture) + (state.OOMKilled ? " (out of memory)" : string.Empty));
        }

        if (!string.IsNullOrEmpty(state?.Error))
        {
            Row(sb, "Error", state.Error);
        }

        Row(sb, "Image", c.Config?.Image);
        Row(sb, "ID", Format.ShortId(c.Id));
        Row(sb, "Platform", c.Platform);
        Row(sb, "Created", Format.Ago(c.Created));
        Row(sb, state?.Running == true ? "Started" : "Finished", Format.Ago(state?.Running == true ? state.StartedAt : state?.FinishedAt));
        Row(sb, "Restart policy", c.HostConfig?.RestartPolicy?.Name is { Length: > 0 } policy ? $"{policy} (restarted {c.RestartCount}×)" : "no");
        Row(sb, "Command", string.Join(' ', new[] { c.Path }.Concat(c.Args ?? []).Where(s => !string.IsNullOrEmpty(s))));
        Row(sb, "Working directory", c.Config?.WorkingDir);
        Row(sb, "User", c.Config?.User is { Length: > 0 } user ? user : "default");
        if (usage is not null)
        {
            sb.Append(usage);
        }

        if (c.NetworkSettings?.Networks is { Count: > 0 } networks)
        {
            sb.AppendLine().AppendLine("## Networks").AppendLine("| Network | IP address | Gateway |").AppendLine("|---|---|---|");
            foreach (var (name, n) in networks)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"| {Format.Md(name)} | {Format.Md(n.IPAddress)} | {Format.Md(n.Gateway)} |");
            }
        }

        if (c.Mounts is { Count: > 0 } mounts)
        {
            sb.AppendLine().AppendLine("## Mounts").AppendLine("| Type | Source | Destination | Access |").AppendLine("|---|---|---|---|");
            foreach (var m in mounts)
            {
                var source = m.Type == "volume" ? m.Name : m.Source;
                sb.AppendLine(CultureInfo.InvariantCulture, $"| {m.Type} | {Format.Md(source)} | {Format.Md(m.Destination)} | {(m.RW ? "read/write" : "read-only")} |");
            }
        }

        if (c.Config?.Env is { Count: > 0 } env)
        {
            // Values often hold secrets; only names are shown.
            sb.AppendLine().AppendLine("## Environment").AppendLine();
            sb.AppendLine(string.Join(", ", env.Select(e => $"`{e.Split('=', 2)[0]}`")));
        }

        if (c.Config?.Labels is { Count: > 0 } labels)
        {
            sb.AppendLine().AppendLine("## Labels").AppendLine("| Label | Value |").AppendLine("|---|---|");
            foreach (var (key, value) in labels.OrderBy(l => l.Key, StringComparer.Ordinal))
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"| {Format.Md(key)} | {Format.Md(value)} |");
            }
        }

        return sb.ToString();
    }

    private static void Row(StringBuilder sb, string key, string? value) =>
        sb.AppendLine(CultureInfo.InvariantCulture, $"| {key} | {(string.IsNullOrEmpty(value) ? "—" : Format.Md(value))} |");
}
