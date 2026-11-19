using System;
using System.Collections.Generic;
using System.Linq;

namespace DockerExtension.Docker;

internal static class ContainerExtensions
{
    public const string ComposeProjectLabel = "com.docker.compose.project";
    public const string ComposeServiceLabel = "com.docker.compose.service";
    public const string ComposeWorkingDirLabel = "com.docker.compose.project.working_dir";
    public const string ComposeConfigFilesLabel = "com.docker.compose.project.config_files";

    public static string DisplayName(this ContainerSummary c) =>
        c.Names is { Count: > 0 } names ? names[0].TrimStart('/') : Helpers.Format.ShortId(c.Id);

    public static bool IsRunning(this ContainerSummary c) => c.State is "running" or "restarting";

    public static bool IsPaused(this ContainerSummary c) => c.State == "paused";

    public static string? Label(this ContainerSummary c, string key) =>
        c.Labels is not null && c.Labels.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value) ? value : null;

    public static string? ComposeProject(this ContainerSummary c) => c.Label(ComposeProjectLabel);

    public static string? ComposeService(this ContainerSummary c) => c.Label(ComposeServiceLabel);

    /// <summary>"healthy", "unhealthy", "starting" or null, parsed from the human-readable status.</summary>
    public static string? Health(this ContainerSummary c) =>
        c.Status.Contains("(unhealthy)", StringComparison.Ordinal) ? "unhealthy"
        : c.Status.Contains("(healthy)", StringComparison.Ordinal) ? "healthy"
        : c.Status.Contains("health: starting", StringComparison.Ordinal) ? "starting"
        : null;

    /// <summary>Published TCP ports as browser URLs, one per host port.</summary>
    public static IReadOnlyList<(int HostPort, int ContainerPort, string Url)> WebPorts(this ContainerSummary c)
    {
        if (c.Ports is null)
        {
            return [];
        }

        return c.Ports
            .Where(p => p.PublicPort is > 0 && p.Type == "tcp")
            .GroupBy(p => p.PublicPort!.Value)
            .Select(g =>
            {
                var p = g.First();
                var host = string.IsNullOrEmpty(p.IP) || p.IP is "0.0.0.0" or "::" ? "localhost" : (p.IP.Contains(':') ? $"[{p.IP}]" : p.IP);
                var scheme = p.PrivatePort is 443 or 8443 || g.Key is 443 or 8443 ? "https" : "http";
                return (g.Key, p.PrivatePort, $"{scheme}://{host}:{g.Key}");
            })
            .OrderBy(p => p.Key)
            .ToList();
    }

    public static string PortsSummary(this ContainerSummary c)
    {
        if (c.Ports is null || c.Ports.Count == 0)
        {
            return string.Empty;
        }

        var published = c.Ports
            .Where(p => p.PublicPort is > 0)
            .Select(p => $"{p.PublicPort}→{p.PrivatePort}{(p.Type == "tcp" ? string.Empty : "/" + p.Type)}")
            .Distinct()
            .ToList();
        return string.Join(", ", published);
    }

    public static IEnumerable<string> NetworkNames(this ContainerSummary c) =>
        c.NetworkSettings?.Networks?.Keys ?? Enumerable.Empty<string>();

    public static string? IpAddress(this ContainerSummary c) =>
        c.NetworkSettings?.Networks?.Values.Select(n => n.IPAddress).FirstOrDefault(ip => !string.IsNullOrEmpty(ip));
}
