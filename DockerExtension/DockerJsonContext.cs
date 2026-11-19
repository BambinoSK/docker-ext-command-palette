using System.Collections.Generic;
using System.Text.Json.Serialization;
using DockerExtension.Docker;

namespace DockerExtension;

[JsonSerializable(typeof(List<ContainerSummary>))]
[JsonSerializable(typeof(ContainerInspect))]
[JsonSerializable(typeof(ContainerStats))]
[JsonSerializable(typeof(List<ImageSummary>))]
[JsonSerializable(typeof(List<ImageDeleteItem>))]
[JsonSerializable(typeof(VolumeListResponse))]
[JsonSerializable(typeof(List<NetworkSummary>))]
[JsonSerializable(typeof(SystemVersion))]
[JsonSerializable(typeof(SystemInfo))]
[JsonSerializable(typeof(SystemDf))]
[JsonSerializable(typeof(PruneResponse))]
[JsonSerializable(typeof(DockerEvent))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(StreamProgress))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, NumberHandling = JsonNumberHandling.AllowReadingFromString)]
internal sealed partial class DockerJsonContext : JsonSerializerContext
{
}
