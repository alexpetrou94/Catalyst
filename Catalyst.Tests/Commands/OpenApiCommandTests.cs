using Catalyst.Common.Configuration;
using Catalyst.Commands.Gen;

namespace Catalyst.Tests.Commands;

public class OpenApiCommandTests
{
    private static CatalystConfig ConfigWith(params string[] skipPaths) => new CatalystConfig
    {
        Gen = new GenConfig
        {
            OpenApi = new OpenApiGenConfig
            {
                SkipPaths = skipPaths.ToList()
            }
        }
    };

    [Fact]
    public void Insecure_DefaultsToFalse_SoCertificateValidationStaysOn()
    {
        OpenApiCommand.Settings settings = new();

        Assert.False(settings.Insecure);
    }

    [Fact]
    public void ResolveSkipPaths_UsesCli_WhenProvided()
    {
        List<string> result = OpenApiCommand.ResolveSkipPaths(["/cli/"], ConfigWith("/cfg/"));

        Assert.Equal(["/cli/"], result);
    }

    [Fact]
    public void ResolveSkipPaths_UsesConfig_WhenCliAbsent()
    {
        List<string> result = OpenApiCommand.ResolveSkipPaths(null, ConfigWith("/cfg/"));

        Assert.Equal(["/cfg/"], result);
    }

    [Fact]
    public void ResolveSkipPaths_ReturnsEmpty_WhenNeitherProvided()
    {
        List<string> result = OpenApiCommand.ResolveSkipPaths(null, null);

        Assert.Empty(result);
    }

    [Fact]
    public void ResolveSkipPaths_SplitsCommaSeparatedCliValues()
    {
        List<string> result = OpenApiCommand.ResolveSkipPaths(["/a/, /b/"], null);

        Assert.Equal(["/a/", "/b/"], result);
    }

    [Fact]
    public void ResolveSkipPaths_SupportsRepeatableCliValues()
    {
        List<string> result = OpenApiCommand.ResolveSkipPaths(["/a/", "/b/"], null);

        Assert.Equal(["/a/", "/b/"], result);
    }

    [Fact]
    public void ResolveOutputPath_UsesClassName_WhenOutputEndsWithSeparator()
    {
        string result = OpenApiCommand.ResolveOutputPath("src\\external-apis\\", "CommunicationApi");

        Assert.Equal(Path.Combine("src\\external-apis", "CommunicationApi.ts"), result);
    }

    [Fact]
    public void ResolveOutputPath_DefaultsToApi_WhenOutputIsDirectoryAndNoClassName()
    {
        string result = OpenApiCommand.ResolveOutputPath("src\\external-apis\\", null);

        Assert.Equal(Path.Combine("src\\external-apis", "api.ts"), result);
    }

    [Fact]
    public void ResolveOutputPath_TreatsExistingDirectoryAsDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string result = OpenApiCommand.ResolveOutputPath(dir, "CommunicationApi");

            Assert.Equal(Path.Combine(dir, "CommunicationApi.ts"), result);
        }
        finally
        {
            Directory.Delete(dir);
        }
    }

    [Fact]
    public void ResolveOutputPath_PassesThroughPlainFilePath()
    {
        string result = OpenApiCommand.ResolveOutputPath("src\\external-apis\\api.ts", "CommunicationApi");

        Assert.Equal("src\\external-apis\\api.ts", result);
    }

    [Fact]
    public void ResolveOutputPath_SanitizesInvalidFileNameCharacters()
    {
        string result = OpenApiCommand.ResolveOutputPath("out\\", "My:Api*?");

        Assert.Equal(Path.Combine("out", "My_Api__.ts"), result);
    }

    [Fact]
    public void ResolveOutputPath_ReturnsEmpty_WhenOutputIsBlank()
    {
        Assert.Equal(string.Empty, OpenApiCommand.ResolveOutputPath("", null));
        Assert.Equal(string.Empty, OpenApiCommand.ResolveOutputPath("   ", null));
    }
}
