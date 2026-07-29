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
}
