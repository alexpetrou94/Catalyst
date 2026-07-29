using Catalyst.Common.Configuration;
using System.Text.Json;

namespace Catalyst.Tests.Services;

public class ConfigLoaderTests
{
    private static string WriteConfig(string directory, string fileName, CatalystConfig config)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, fileName);
        File.WriteAllText(path, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    [Fact]
    public void Load_ReturnsNull_WhenNoConfigFilePresent()
    {
        string dir = Path.Combine(Path.GetTempPath(), "catalyst-config-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);

        try
        {
            Assert.Null(ConfigLoader.Load(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_ReadsSkipPaths_FromCatalystConfigJson()
    {
        string dir = Path.Combine(Path.GetTempPath(), "catalyst-config-tests", Guid.NewGuid().ToString());
        WriteConfig(dir, "catalyst.config.json", new CatalystConfig
        {
            Gen = new GenConfig
            {
                OpenApi = new OpenApiGenConfig
                {
                    SkipPaths = ["/api/auth/", "/internal/"]
                }
            }
        });

        try
        {
            CatalystConfig? loaded = ConfigLoader.Load(dir);

            Assert.NotNull(loaded);
            Assert.Equal(["/api/auth/", "/internal/"], loaded!.Gen.OpenApi.SkipPaths);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_ReadsSkipPaths_FromHiddenConfigFile()
    {
        string dir = Path.Combine(Path.GetTempPath(), "catalyst-config-tests", Guid.NewGuid().ToString());
        WriteConfig(dir, ".catalyst.config.json", new CatalystConfig
        {
            Gen = new GenConfig
            {
                OpenApi = new OpenApiGenConfig
                {
                    SkipPaths = ["/x/"]
                }
            }
        });

        try
        {
            CatalystConfig? loaded = ConfigLoader.Load(dir);

            Assert.NotNull(loaded);
            Assert.Contains("/x/", loaded!.Gen.OpenApi.SkipPaths);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_WalksUpToParentDirectory()
    {
        string parent = Path.Combine(Path.GetTempPath(), "catalyst-config-tests", Guid.NewGuid().ToString());
        string child = Path.Combine(parent, "sub", "deep");
        WriteConfig(parent, "catalyst.config.json", new CatalystConfig
        {
            Gen = new GenConfig
            {
                OpenApi = new OpenApiGenConfig
                {
                    SkipPaths = ["/parent-only/"]
                }
            }
        });

        try
        {
            CatalystConfig? loaded = ConfigLoader.Load(child);

            Assert.NotNull(loaded);
            Assert.Contains("/parent-only/", loaded!.Gen.OpenApi.SkipPaths);
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public void Load_ReadsFixtureConfigFile()
    {
        string fixtureDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!;
        string fixturePath = Path.Combine(fixtureDir, "Fixtures", "catalyst.config.json");

        if (!File.Exists(fixturePath))
        {
            return;
        }

        CatalystConfig? loaded = ConfigLoader.Load(Path.Combine(fixtureDir, "Fixtures"));

        Assert.NotNull(loaded);
        Assert.NotEmpty(loaded!.Gen.OpenApi.SkipPaths);
    }
}
