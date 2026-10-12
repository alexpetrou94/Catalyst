using Catalyst.Commands.Gen;
using Catalyst.Common.Configuration;

namespace Catalyst.Tests.Commands;

public class ConfiguredClientRunnerTests
{
    private const string Spec = """
        {
          "openapi": "3.0.3",
          "info": { "title": "Runner API", "version": "1.0.0" },
          "paths": {
            "/keep": { "get": { "responses": { "200": { "description": "ok" } } } },
            "/skip": { "get": { "responses": { "200": { "description": "ok" } } } }
          }
        }
        """;

    private static string CreateTempDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), "catalyst-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static CatalystConfig ConfigWith(params OpenApiClientConfig[] clients) => new()
    {
        Gen = new GenConfig
        {
            OpenApi = new OpenApiGenConfig
            {
                Clients = clients.ToList()
            }
        }
    };

    [Fact]
    public async Task RunAsync_GeneratesEveryConfiguredClient_RelativeToTheConfigDirectory()
    {
        string dir = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(dir, "openapi.json"), Spec);

            CatalystConfig config = ConfigWith(
                new OpenApiClientConfig { Name = "FirstApi", Source = "openapi.json", Output = "clients" },
                new OpenApiClientConfig { Name = "SecondApi", Source = "openapi.json", Output = Path.Combine("clients", "second.ts") });

            List<ConfiguredClientOutcome> outcomes = await ConfiguredClientRunner.RunAsync(config, dir, CancellationToken.None);

            Assert.All(outcomes, (outcome) => Assert.True(outcome.Success, outcome.Error));

            string first = Path.Combine(dir, "clients", "FirstApi.ts");
            string second = Path.Combine(dir, "clients", "second.ts");
            Assert.True(File.Exists(first));
            Assert.True(File.Exists(second));
            Assert.Equal(first, outcomes[0].OutputPath);
            Assert.Equal(second, outcomes[1].OutputPath);
            Assert.Contains("FirstApi", File.ReadAllText(first));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_FallsBackToProjectSkipPaths_WhenTheClientHasNone()
    {
        string dir = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(dir, "openapi.json"), Spec);

            CatalystConfig config = new()
            {
                Gen = new GenConfig
                {
                    OpenApi = new OpenApiGenConfig
                    {
                        SkipPaths = ["/skip"],
                        Clients = [new OpenApiClientConfig { Name = "Api", Source = "openapi.json", Output = "out" }]
                    }
                }
            };

            List<ConfiguredClientOutcome> outcomes = await ConfiguredClientRunner.RunAsync(config, dir, CancellationToken.None);

            Assert.True(Assert.Single(outcomes).Success);
            string code = File.ReadAllText(Path.Combine(dir, "out", "Api.ts"));
            Assert.Contains("/keep", code);
            Assert.DoesNotContain("/skip", code);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_ClientSkipPaths_TakePrecedenceOverProjectSkipPaths()
    {
        string dir = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(dir, "openapi.json"), Spec);

            CatalystConfig config = new()
            {
                Gen = new GenConfig
                {
                    OpenApi = new OpenApiGenConfig
                    {
                        SkipPaths = ["/keep"],
                        Clients =
                        [
                            new OpenApiClientConfig
                            {
                                Name = "Api",
                                Source = "openapi.json",
                                Output = "out",
                                SkipPaths = ["/skip"]
                            }
                        ]
                    }
                }
            };

            List<ConfiguredClientOutcome> outcomes = await ConfiguredClientRunner.RunAsync(config, dir, CancellationToken.None);

            Assert.True(Assert.Single(outcomes).Success);
            string code = File.ReadAllText(Path.Combine(dir, "out", "Api.ts"));
            Assert.Contains("/keep", code);
            Assert.DoesNotContain("/skip", code);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_ReportsAClientWithoutSource_AndStillGeneratesTheOthers()
    {
        string dir = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(dir, "openapi.json"), Spec);

            CatalystConfig config = ConfigWith(
                new OpenApiClientConfig { Name = "ComesLater", Output = "out" },
                new OpenApiClientConfig { Name = "Works", Source = "openapi.json", Output = "out" });

            List<ConfiguredClientOutcome> outcomes = await ConfiguredClientRunner.RunAsync(config, dir, CancellationToken.None);

            Assert.False(outcomes[0].Success);
            Assert.Contains("No source", outcomes[0].Error);
            Assert.True(outcomes[1].Success);
            Assert.True(File.Exists(Path.Combine(dir, "out", "Works.ts")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_ReportsEntriesWithoutNameOrOutput()
    {
        string dir = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(dir, "openapi.json"), Spec);

            CatalystConfig config = ConfigWith(
                new OpenApiClientConfig { Name = "", Source = "openapi.json", Output = "out" },
                new OpenApiClientConfig { Name = "NoOutput", Source = "openapi.json", Output = "  " });

            List<ConfiguredClientOutcome> outcomes = await ConfiguredClientRunner.RunAsync(config, dir, CancellationToken.None);

            Assert.All(outcomes, (outcome) => Assert.False(outcome.Success));
            Assert.Equal("(unnamed)", outcomes[0].Name);
            Assert.Contains("name and an output", outcomes[1].Error);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_ReportsAMissingSourceFile()
    {
        string dir = CreateTempDirectory();
        try
        {
            CatalystConfig config = ConfigWith(
                new OpenApiClientConfig { Name = "Api", Source = "missing.json", Output = "out" });

            List<ConfiguredClientOutcome> outcomes = await ConfiguredClientRunner.RunAsync(config, dir, CancellationToken.None);

            ConfiguredClientOutcome outcome = Assert.Single(outcomes);
            Assert.False(outcome.Success);
            Assert.Contains("File not found", outcome.Error);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
