using System.Globalization;
using System.Text.Json;
using DockerExtension.Docker;
using DockerExtension.Helpers;
using DockerExtension.Pages;

namespace DockerExtension.Tests;

public class HelperTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "\"\"")]
    [InlineData("two words", "\"two words\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData(@"C:\dir with space\", "\"C:\\dir with space\\\\\"")]
    [InlineData("a&b", "\"a&b\"")]
    [InlineData(@"C:\no\spaces", @"C:\no\spaces")]
    public void Quote_FollowsWindowsArgvRules(string arg, string expected) =>
        Assert.Equal(expected, TerminalLauncher.Quote(arg));

    [Fact]
    public void Quote_DefaultLinuxShellSurvivesAsOneArgument() =>
        Assert.Equal(
            "\"command -v bash >/dev/null && exec bash || exec sh\"",
            TerminalLauncher.Quote("command -v bash >/dev/null && exec bash || exec sh"));

    private const string DockerLine = "\"C:\\Program Files\\Docker\\docker.exe\" logs web";

    [Fact]
    public void CustomTerminal_PassesCommandAsOneQuotedArgument()
    {
        var psi = TerminalLauncher.CustomTerminal("\"C:\\Program Files\\Git\\bin\\bash.exe\" -lc {command}", "web — logs", DockerLine);

        Assert.Equal("C:\\Program Files\\Git\\bin\\bash.exe", psi.FileName);
        Assert.Equal("-lc \"\\\"C:\\Program Files\\Docker\\docker.exe\\\" logs web\"", psi.Arguments);
    }

    [Fact]
    public void CustomTerminal_AppendsCommandWhenPlaceholderIsMissing()
    {
        var psi = TerminalLauncher.CustomTerminal("alacritty --title {title} -e", "web — logs", DockerLine);

        Assert.Equal("alacritty", psi.FileName);
        Assert.Equal($"--title \"web — logs\" -e {DockerLine}", psi.Arguments);
    }

    [Fact]
    public void CustomFolderTerminal_FillsDirectoryAndTitle()
    {
        var psi = TerminalLauncher.CustomFolderTerminal("alacritty --working-directory {directory} --title {title}", "shop", "C:\\Projects\\My Shop");

        Assert.Equal("alacritty", psi.FileName);
        Assert.Equal("--working-directory \"C:\\Projects\\My Shop\" --title shop", psi.Arguments);
    }

    [Fact]
    public void CustomTerminal_RequiresATemplate() =>
        Assert.Throws<InvalidOperationException>(() => TerminalLauncher.CustomTerminal(string.Empty, "t", DockerLine));

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(999, "999 B")]
    [InlineData(1500, "1.5 KB")]
    [InlineData(250_000_000, "250 MB")]
    [InlineData(3_200_000_000, "3.2 GB")]
    [InlineData(-1, "—")]
    public void Bytes_UsesDecimalUnits(long bytes, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Assert.Equal(expected, Format.Bytes(bytes));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("sha256:0123456789abcdef", "0123456789ab")]
    [InlineData("abc", "abc")]
    public void ShortId_StripsDigestPrefix(string id, string expected) => Assert.Equal(expected, Format.ShortId(id));

    [Fact]
    public void Ago_HandlesZeroDateFromEngine() => Assert.Equal("—", Format.Ago("0001-01-01T00:00:00Z"));

    [Fact]
    public void Md_EscapesTableAndEmphasisCharacters() =>
        Assert.Equal(@"a\|b \*c\* \_d\_ \`e\` &lt;f>", Format.Md("a|b *c* _d_ `e` <f>"));

    [Fact]
    public void ContextList_ParsesJsonLines()
    {
        const string output = """
            {"Current":false,"Description":"Create a context","DockerEndpoint":"npipe:////./pipe/docker_engine","Name":"default"}
            {"Current":true,"Description":"Docker Desktop","DockerEndpoint":"npipe:////./pipe/dockerDesktopLinuxEngine","Name":"desktop-linux"}
            """;

        var contexts = SystemPage.ContextEntry.Parse(output);

        Assert.Equal(2, contexts.Count);
        Assert.Equal("desktop-linux", Assert.Single(contexts, c => c.Current).Name);
        Assert.Equal("npipe:////./pipe/docker_engine", contexts[0].Endpoint);
    }

    [Fact]
    public void ContextList_ParsesJsonArray()
    {
        const string output = """[{"Name":"default","Current":true,"DockerEndpoint":"npipe:////./pipe/docker_engine"}]""";

        var context = Assert.Single(SystemPage.ContextEntry.Parse(output));
        Assert.True(context.Current);
        Assert.Null(context.Description);
    }

    [Fact]
    public void PruneResponse_CountsEveryDeletedList()
    {
        const string json = """{"ContainersDeleted":["a","b"],"ImagesDeleted":[{"Untagged":"x"},{"Deleted":"sha256:y"}],"SpaceReclaimed":1024}""";

        var response = JsonSerializer.Deserialize(json, DockerJsonContext.Default.PruneResponse)!;

        Assert.Equal(1024, response.SpaceReclaimed);
        Assert.Equal(4, response.DeletedCount);
    }
}
