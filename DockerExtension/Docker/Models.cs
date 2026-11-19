using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DockerExtension.Docker;

// Subsets of the Docker Engine API schemas; only fields the extension reads.

internal sealed class ContainerSummary
{
    public string Id { get; set; } = string.Empty;
    public List<string>? Names { get; set; }
    public string Image { get; set; } = string.Empty;
    public string ImageID { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public long Created { get; set; }
    public List<PortMapping>? Ports { get; set; }
    public Dictionary<string, string>? Labels { get; set; }
    public string State { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public SummaryNetworkSettings? NetworkSettings { get; set; }
    public List<MountPoint>? Mounts { get; set; }
}

internal sealed class PortMapping
{
    public string? IP { get; set; }
    public int PrivatePort { get; set; }
    public int? PublicPort { get; set; }
    public string Type { get; set; } = string.Empty;
}

internal sealed class SummaryNetworkSettings
{
    public Dictionary<string, EndpointSettings>? Networks { get; set; }
}

internal sealed class EndpointSettings
{
    public string? IPAddress { get; set; }
    public string? Gateway { get; set; }
    public string? MacAddress { get; set; }
}

internal sealed class MountPoint
{
    public string? Type { get; set; }
    public string? Name { get; set; }
    public string? Source { get; set; }
    public string? Destination { get; set; }
    public string? Mode { get; set; }
    public bool RW { get; set; }
}

internal sealed class ContainerInspect
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Created { get; set; }
    public string? Path { get; set; }
    public List<string>? Args { get; set; }
    public ContainerState? State { get; set; }
    public string? Image { get; set; }
    public int RestartCount { get; set; }
    public string? Platform { get; set; }
    public ContainerConfig? Config { get; set; }
    public HostConfig? HostConfig { get; set; }
    public List<MountPoint>? Mounts { get; set; }
    public InspectNetworkSettings? NetworkSettings { get; set; }
}

internal sealed class ContainerState
{
    public string? Status { get; set; }
    public bool Running { get; set; }
    public bool Paused { get; set; }
    public bool Restarting { get; set; }
    public bool OOMKilled { get; set; }
    public int Pid { get; set; }
    public int ExitCode { get; set; }
    public string? Error { get; set; }
    public string? StartedAt { get; set; }
    public string? FinishedAt { get; set; }
    public HealthState? Health { get; set; }
}

internal sealed class HealthState
{
    public string? Status { get; set; }
    public int FailingStreak { get; set; }
}

internal sealed class ContainerConfig
{
    public string? Hostname { get; set; }
    public string? User { get; set; }
    public List<string>? Env { get; set; }
    public List<string>? Cmd { get; set; }
    public List<string>? Entrypoint { get; set; }
    public string? Image { get; set; }
    public string? WorkingDir { get; set; }
    public Dictionary<string, string>? Labels { get; set; }
}

internal sealed class HostConfig
{
    public RestartPolicy? RestartPolicy { get; set; }
}

internal sealed class RestartPolicy
{
    public string? Name { get; set; }
}

internal sealed class InspectNetworkSettings
{
    public Dictionary<string, EndpointSettings>? Networks { get; set; }
}

internal sealed class ContainerStats
{
    [JsonPropertyName("cpu_stats")]
    public CpuStats? CpuStats { get; set; }

    [JsonPropertyName("precpu_stats")]
    public CpuStats? PreCpuStats { get; set; }

    [JsonPropertyName("memory_stats")]
    public MemoryStats? MemoryStats { get; set; }

    [JsonPropertyName("num_procs")]
    public int NumProcs { get; set; }
}

internal sealed class CpuStats
{
    [JsonPropertyName("cpu_usage")]
    public CpuUsage? CpuUsage { get; set; }

    [JsonPropertyName("system_cpu_usage")]
    public ulong SystemCpuUsage { get; set; }

    [JsonPropertyName("online_cpus")]
    public uint OnlineCpus { get; set; }
}

internal sealed class CpuUsage
{
    [JsonPropertyName("total_usage")]
    public ulong TotalUsage { get; set; }

    [JsonPropertyName("percpu_usage")]
    public List<ulong>? PerCpuUsage { get; set; }
}

internal sealed class MemoryStats
{
    [JsonPropertyName("usage")]
    public ulong Usage { get; set; }

    [JsonPropertyName("limit")]
    public ulong Limit { get; set; }

    [JsonPropertyName("stats")]
    public Dictionary<string, ulong>? Stats { get; set; }

    // Windows containers report private working set instead of usage/limit.
    [JsonPropertyName("privateworkingset")]
    public ulong PrivateWorkingSet { get; set; }
}

internal sealed class ImageSummary
{
    public string Id { get; set; } = string.Empty;
    public List<string>? RepoTags { get; set; }
    public List<string>? RepoDigests { get; set; }
    public long Created { get; set; }
    public long Size { get; set; }
    public long Containers { get; set; }
    public Dictionary<string, string>? Labels { get; set; }
}

internal sealed class VolumeListResponse
{
    public List<Volume>? Volumes { get; set; }
}

internal sealed class Volume
{
    public string Name { get; set; } = string.Empty;
    public string? Driver { get; set; }
    public string? Mountpoint { get; set; }
    public string? CreatedAt { get; set; }
    public string? Scope { get; set; }
    public Dictionary<string, string>? Labels { get; set; }
    public VolumeUsageData? UsageData { get; set; }
}

internal sealed class VolumeUsageData
{
    public long Size { get; set; }
    public long RefCount { get; set; }
}

internal sealed class NetworkSummary
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Driver { get; set; }
    public string? Scope { get; set; }
    public string? Created { get; set; }
    public bool Internal { get; set; }
    public bool Attachable { get; set; }
    public Ipam? IPAM { get; set; }
    public Dictionary<string, string>? Labels { get; set; }
}

internal sealed class Ipam
{
    public List<IpamConfig>? Config { get; set; }
}

internal sealed class IpamConfig
{
    public string? Subnet { get; set; }
    public string? Gateway { get; set; }
}

internal sealed class SystemVersion
{
    public string? Version { get; set; }
    public string? ApiVersion { get; set; }
    public string? Os { get; set; }
    public string? Arch { get; set; }
    public string? KernelVersion { get; set; }
    public PlatformInfo? Platform { get; set; }
}

internal sealed class PlatformInfo
{
    public string? Name { get; set; }
}

internal sealed class SystemInfo
{
    public string? Name { get; set; }
    public string? ServerVersion { get; set; }
    public string? OperatingSystem { get; set; }
    public string? OSType { get; set; }
    public string? Architecture { get; set; }
    public int NCPU { get; set; }
    public long MemTotal { get; set; }
    public int Containers { get; set; }
    public int ContainersRunning { get; set; }
    public int ContainersPaused { get; set; }
    public int ContainersStopped { get; set; }
    public int Images { get; set; }
}

internal sealed class SystemDf
{
    public long LayersSize { get; set; }
    public List<DfImage>? Images { get; set; }
    public List<DfContainer>? Containers { get; set; }
    public List<Volume>? Volumes { get; set; }
    public List<DfBuildCache>? BuildCache { get; set; }
}

internal sealed class DfImage
{
    public long Size { get; set; }
    public long SharedSize { get; set; }
    public long Containers { get; set; }
}

internal sealed class DfContainer
{
    public long SizeRw { get; set; }
    public string? State { get; set; }
}

internal sealed class DfBuildCache
{
    public long Size { get; set; }
    public bool InUse { get; set; }
    public bool Shared { get; set; }
}

internal sealed class PruneResponse
{
    public List<string>? ContainersDeleted { get; set; }
    public List<string>? VolumesDeleted { get; set; }
    public List<string>? NetworksDeleted { get; set; }
    public List<string>? CachesDeleted { get; set; }
    public List<ImageDeleteItem>? ImagesDeleted { get; set; }
    public long SpaceReclaimed { get; set; }

    public int DeletedCount =>
        (ContainersDeleted?.Count ?? 0) + (VolumesDeleted?.Count ?? 0) + (NetworksDeleted?.Count ?? 0) +
        (CachesDeleted?.Count ?? 0) + (ImagesDeleted?.Count ?? 0);
}

internal sealed class ImageDeleteItem
{
    public string? Untagged { get; set; }
    public string? Deleted { get; set; }
}

internal sealed class DockerEvent
{
    public string? Type { get; set; }
    public string? Action { get; set; }
}

internal sealed class ErrorResponse
{
    public string? Message { get; set; }
}

internal sealed class StreamProgress
{
    public string? Status { get; set; }
    public string? Error { get; set; }
}
