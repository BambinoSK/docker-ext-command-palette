using System.Text.Json;
using DockerExtension.Commands;
using DockerExtension.Docker;

namespace DockerExtension.Tests;

public class ContainerTests
{
    // Trimmed from a real `GET /containers/json?all=true` response.
    private const string ContainersJson = """
        [
          {
            "Id": "8dfafdbc3a40aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "Names": ["/shop-web-1"],
            "Image": "nginx:alpine",
            "ImageID": "sha256:abc",
            "Command": "/docker-entrypoint.sh nginx -g 'daemon off;'",
            "Created": 1700000000,
            "Ports": [
              {"IP": "0.0.0.0", "PrivatePort": 80, "PublicPort": 8080, "Type": "tcp"},
              {"IP": "::", "PrivatePort": 80, "PublicPort": 8080, "Type": "tcp"},
              {"IP": "127.0.0.1", "PrivatePort": 443, "PublicPort": 9443, "Type": "tcp"},
              {"PrivatePort": 53, "PublicPort": 5353, "Type": "udp"},
              {"PrivatePort": 9000, "Type": "tcp"}
            ],
            "Labels": {
              "com.docker.compose.project": "shop",
              "com.docker.compose.service": "web",
              "com.docker.compose.project.working_dir": "C:\\src\\shop",
              "com.docker.compose.project.config_files": "C:\\src\\shop\\compose.yaml, C:\\src\\shop\\compose.override.yaml"
            },
            "State": "running",
            "Status": "Up 2 hours (healthy)",
            "NetworkSettings": {"Networks": {"shop_default": {"IPAddress": "172.18.0.2", "Gateway": "172.18.0.1"}}},
            "Mounts": []
          },
          {
            "Id": "11111111111111111111111111111111",
            "Names": ["/shop-db-1"],
            "Image": "postgres:16",
            "Created": 1700000000,
            "Ports": [],
            "Labels": {"com.docker.compose.project": "shop", "com.docker.compose.service": "db"},
            "State": "exited",
            "Status": "Exited (1) 3 minutes ago"
          },
          {
            "Id": "22222222222222222222222222222222",
            "Names": ["/standalone"],
            "Image": "redis",
            "Created": 1700000000,
            "Labels": {},
            "State": "paused",
            "Status": "Up 5 minutes (Paused)"
          }
        ]
        """;

    private static List<ContainerSummary> Load() =>
        JsonSerializer.Deserialize(ContainersJson, DockerJsonContext.Default.ListContainerSummary)!;

    [Fact]
    public void Deserializes_EngineResponse()
    {
        var containers = Load();

        Assert.Equal(3, containers.Count);
        Assert.Equal("shop-web-1", containers[0].DisplayName());
        Assert.Equal("shop", containers[0].ComposeProject());
        Assert.Equal("web", containers[0].ComposeService());
        Assert.Equal("172.18.0.2", containers[0].IpAddress());
        Assert.Equal(["shop_default"], containers[0].NetworkNames());
    }

    [Fact]
    public void States_AreClassified()
    {
        var containers = Load();

        Assert.True(containers[0].IsRunning());
        Assert.False(containers[1].IsRunning());
        Assert.False(containers[1].IsPaused());
        Assert.True(containers[2].IsPaused());
        Assert.False(containers[2].IsRunning());
    }

    [Theory]
    [InlineData("Up 2 hours (healthy)", "healthy")]
    [InlineData("Up 1 minute (unhealthy)", "unhealthy")]
    [InlineData("Up 3 seconds (health: starting)", "starting")]
    [InlineData("Up 2 hours", null)]
    public void Health_IsParsedFromStatus(string status, string? expected) =>
        Assert.Equal(expected, new ContainerSummary { Status = status }.Health());

    [Fact]
    public void WebPorts_DeduplicatesIpv4AndIpv6AndPicksScheme()
    {
        var ports = Load()[0].WebPorts();

        Assert.Equal(
            [(8080, 80, "http://localhost:8080"), (9443, 443, "https://127.0.0.1:9443")],
            ports);
    }

    [Fact]
    public void PortsSummary_ListsPublishedPortsOnce()
    {
        Assert.Equal("8080→80, 9443→443, 5353→53/udp", Load()[0].PortsSummary());
        Assert.Equal(string.Empty, Load()[1].PortsSummary());
    }

    [Fact]
    public void DisplayName_FallsBackToShortId()
    {
        var c = new ContainerSummary { Id = "0123456789abcdef0123" };

        Assert.Equal("0123456789ab", c.DisplayName());
    }

    [Fact]
    public void ComposeProjects_GroupByProjectLabel()
    {
        var projects = ComposeProject.FromContainers(Load());

        var shop = Assert.Single(projects);
        Assert.Equal("shop", shop.Name);
        Assert.Equal(@"C:\src\shop", shop.WorkingDirectory);
        Assert.Equal([@"C:\src\shop\compose.yaml", @"C:\src\shop\compose.override.yaml"], shop.ConfigFiles);
        Assert.Equal(2, shop.Containers.Count);
        Assert.Equal(1, shop.RunningCount);
    }
}
