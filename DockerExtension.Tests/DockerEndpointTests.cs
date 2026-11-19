using System.Security.Cryptography;
using System.Text;
using DockerExtension.Docker;

namespace DockerExtension.Tests;

public class DockerEndpointTests
{
    [Theory]
    [InlineData("npipe:////./pipe/docker_engine", ".", "docker_engine")]
    [InlineData("npipe:////./pipe/dockerDesktopLinuxEngine", ".", "dockerDesktopLinuxEngine")]
    [InlineData(@"npipe://\\.\pipe\docker_engine", ".", "docker_engine")]
    [InlineData("npipe:////buildhost/pipe/docker_engine", "buildhost", "docker_engine")]
    public void ParsePipe_SplitsServerAndPipeName(string host, string server, string pipe)
    {
        var endpoint = new DockerEndpoint(host, DockerEndpoint.SourceDefault, null);

        Assert.Equal(EndpointKind.NamedPipe, endpoint.Kind);
        Assert.Equal((server, pipe), endpoint.ParsePipe());
    }

    [Fact]
    public void ParsePipe_RejectsPathWithoutPipeSegment()
    {
        var endpoint = new DockerEndpoint("npipe:////./docker_engine", DockerEndpoint.SourceDefault, null);

        Assert.Throws<FormatException>(() => endpoint.ParsePipe());
    }

    [Theory]
    [InlineData("tcp://localhost:2375", nameof(EndpointKind.Tcp))]
    [InlineData("unix:///var/run/docker.sock", nameof(EndpointKind.Unsupported))]
    [InlineData("ssh://user@host", nameof(EndpointKind.Unsupported))]
    public void Kind_FollowsScheme(string host, string kind) =>
        Assert.Equal(Enum.Parse<EndpointKind>(kind), new DockerEndpoint(host, DockerEndpoint.SourceDefault, null).Kind);

    [Fact]
    public void RequiresExplicitCliHost_OnlyForSettingsOverride()
    {
        Assert.True(new DockerEndpoint("tcp://h:2375", DockerEndpoint.SourceSetting, null).RequiresExplicitCliHost);
        Assert.False(new DockerEndpoint("tcp://h:2375", DockerEndpoint.SourceEnvironment, null).RequiresExplicitCliHost);
        Assert.False(new DockerEndpoint("tcp://h:2375", DockerEndpoint.SourceContext, "remote").RequiresExplicitCliHost);
    }

    [Fact]
    public void GlobalArgs_PassesHostOnlyForSettingsOverride()
    {
        Assert.Equal(["-H", "tcp://h:2375"], DockerCli.GlobalArgs(new DockerEndpoint("tcp://h:2375", DockerEndpoint.SourceSetting, null)));
        Assert.Empty(DockerCli.GlobalArgs(new DockerEndpoint("tcp://h:2375", DockerEndpoint.SourceEnvironment, null)));
    }
}

// Resolve reads process-wide environment variables, so these tests must not run in parallel with each other.
[Collection(nameof(ProcessEnvironment))]
public sealed class DockerEndpointResolveTests : IDisposable
{
    private static readonly string[] Variables = ["DOCKER_HOST", "DOCKER_CONTEXT", "DOCKER_CONFIG", "DOCKER_TLS_VERIFY"];

    private readonly Dictionary<string, string?> _saved = Variables.ToDictionary(v => v, Environment.GetEnvironmentVariable);
    private readonly string _configDir = Directory.CreateTempSubdirectory("docker-ext-tests-").FullName;

    public DockerEndpointResolveTests()
    {
        foreach (var name in Variables)
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        Environment.SetEnvironmentVariable("DOCKER_CONFIG", _configDir);
    }

    public void Dispose()
    {
        foreach (var (name, value) in _saved)
        {
            Environment.SetEnvironmentVariable(name, value);
        }

        Directory.Delete(_configDir, recursive: true);
    }

    [Fact]
    public void SettingsOverride_WinsOverEverything()
    {
        Environment.SetEnvironmentVariable("DOCKER_HOST", "tcp://env:2375");

        var endpoint = DockerEndpoint.Resolve("  tcp://override:2375 ");

        Assert.Equal("tcp://override:2375", endpoint.Host);
        Assert.Equal(DockerEndpoint.SourceSetting, endpoint.Source);
    }

    [Fact]
    public void DockerHost_WinsOverContext()
    {
        Environment.SetEnvironmentVariable("DOCKER_HOST", "tcp://env:2375");
        WriteContext("remote", "tcp://ctx:2375", makeCurrent: true);

        var endpoint = DockerEndpoint.Resolve(null);

        Assert.Equal("tcp://env:2375", endpoint.Host);
        Assert.Equal(DockerEndpoint.SourceEnvironment, endpoint.Source);
    }

    [Fact]
    public void CurrentContextFromConfig_IsResolvedThroughMetaFile()
    {
        WriteContext("desktop-linux", "npipe:////./pipe/dockerDesktopLinuxEngine", makeCurrent: true);

        var endpoint = DockerEndpoint.Resolve(null);

        Assert.Equal("npipe:////./pipe/dockerDesktopLinuxEngine", endpoint.Host);
        Assert.Equal(DockerEndpoint.SourceContext, endpoint.Source);
        Assert.Equal("desktop-linux", endpoint.ContextName);
    }

    [Fact]
    public void DockerContextVariable_WinsOverConfigFile()
    {
        WriteContext("desktop-linux", "npipe:////./pipe/dockerDesktopLinuxEngine", makeCurrent: true);
        WriteContext("remote", "tcp://ctx:2375", makeCurrent: false);
        Environment.SetEnvironmentVariable("DOCKER_CONTEXT", "remote");

        Assert.Equal("tcp://ctx:2375", DockerEndpoint.Resolve(null).Host);
    }

    [Fact]
    public void UnknownContext_FallsBackToDefaultPipe()
    {
        Environment.SetEnvironmentVariable("DOCKER_CONTEXT", "missing");

        var endpoint = DockerEndpoint.Resolve(null);

        Assert.Equal(DockerEndpoint.DefaultHost, endpoint.Host);
        Assert.Equal(DockerEndpoint.SourceDefault, endpoint.Source);
    }

    [Fact]
    public void NoConfiguration_UsesDefaultPipe() =>
        Assert.Equal(DockerEndpoint.DefaultHost, DockerEndpoint.Resolve(null).Host);

    [Theory]
    [InlineData(null, "tcp://h", "http://h:2375/")]
    [InlineData("1", "tcp://h", "https://h:2376/")]
    [InlineData("0", "tcp://h:4243", "http://h:4243/")]
    [InlineData("1", "tcp://h:4243", "https://h:4243/")]
    public void ParseTcp_ChoosesSchemeAndDefaultPortFromTlsVerify(string? tlsVerify, string host, string expected)
    {
        Environment.SetEnvironmentVariable("DOCKER_TLS_VERIFY", tlsVerify);

        Assert.Equal(expected, new DockerEndpoint(host, DockerEndpoint.SourceEnvironment, null).ParseTcp().ToString());
    }

    private void WriteContext(string name, string host, bool makeCurrent)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(name)));
        var dir = Directory.CreateDirectory(Path.Combine(_configDir, "contexts", "meta", hash));
        File.WriteAllText(
            Path.Combine(dir.FullName, "meta.json"),
            """{"Name":"NAME","Metadata":{},"Endpoints":{"docker":{"Host":"HOST","SkipTLSVerify":false}}}"""
                .Replace("NAME", name, StringComparison.Ordinal)
                .Replace("HOST", host, StringComparison.Ordinal));

        if (makeCurrent)
        {
            File.WriteAllText(Path.Combine(_configDir, "config.json"), $$"""{"auths":{},"currentContext":"{{name}}"}""");
        }
    }
}

[CollectionDefinition(nameof(ProcessEnvironment), DisableParallelization = true)]
public sealed class ProcessEnvironment;
