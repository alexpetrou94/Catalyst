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

            List<ClientResult> outcomes = await ConfiguredClientRunner.RunAsync(config, dir, CancellationToken.None);

            Assert.All(outcomes, (outcome) => Assert.True(!outcome.Failed, outcome.Error?.Message));

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

            List<ClientResult> outcomes = await ConfiguredClientRunner.RunAsync(config, dir, CancellationToken.None);

            Assert.False(Assert.Single(outcomes).Failed);
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

            List<ClientResult> outcomes = await ConfiguredClientRunner.RunAsync(config, dir, CancellationToken.None);

            Assert.False(Assert.Single(outcomes).Failed);
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

            List<ClientResult> outcomes = await ConfiguredClientRunner.RunAsync(config, dir, CancellationToken.None);

            Assert.True(outcomes[0].Failed);
            Assert.Contains("No source", outcomes[0].Error!.Message);
            Assert.False(outcomes[1].Failed);
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

            List<ClientResult> outcomes = await ConfiguredClientRunner.RunAsync(config, dir, CancellationToken.None);

            Assert.All(outcomes, (outcome) => Assert.True(outcome.Failed));
            Assert.Equal("(unnamed)", outcomes[0].Name);
            Assert.Contains("name and an output", outcomes[1].Error!.Message);
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

            List<ClientResult> outcomes = await ConfiguredClientRunner.RunAsync(config, dir, CancellationToken.None);

            ClientResult outcome = Assert.Single(outcomes);
            Assert.True(outcome.Failed);
            Assert.Contains("File not found", outcome.Error!.Message);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_ContinuesWithTheNextClient_WhenOneThrowsUnexpectedly()
    {
        string dir = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(dir, "openapi.json"), Spec);

            // A NUL in the output path makes the file system throw an ArgumentException, not an IOException.
            CatalystConfig config = ConfigWith(
                new OpenApiClientConfig { Name = "Broken", Source = "openapi.json", Output = "bad\0dir" },
                new OpenApiClientConfig { Name = "Works", Source = "openapi.json", Output = "out" });

            List<ClientResult> outcomes = await ConfiguredClientRunner.RunAsync(config, dir, CancellationToken.None);

            Assert.True(outcomes[0].Failed);
            Assert.Equal("Broken", outcomes[0].Name);
            Assert.False(string.IsNullOrWhiteSpace(outcomes[0].Error!.Message));
            Assert.False(outcomes[1].Failed);
            Assert.True(File.Exists(Path.Combine(dir, "out", "Works.ts")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_ReportsUnchanged_WhenTheFileAlreadyHasTheGeneratedContent()
    {
        string dir = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(dir, "openapi.json"), Spec);
            CatalystConfig config = ConfigWith(new OpenApiClientConfig { Name = "Api", Source = "openapi.json", Output = "out" });

            ClientResult first = Assert.Single(await ConfiguredClientRunner.RunAsync(config, dir, CancellationToken.None));
            ClientResult second = Assert.Single(await ConfiguredClientRunner.RunAsync(config, dir, CancellationToken.None));

            Assert.Equal(ClientStatus.Written, first.Status);
            Assert.Equal(ClientStatus.Unchanged, second.Status);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_ReportsWhatIsWrongWithAMalformedSpec()
    {
        string dir = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(dir, "broken.json"), "{ \"openapi\": ");
            CatalystConfig config = ConfigWith(new OpenApiClientConfig { Name = "Api", Source = "broken.json", Output = "out" });

            ClientResult outcome = Assert.Single(await ConfiguredClientRunner.RunAsync(config, dir, CancellationToken.None));

            Assert.True(outcome.Failed);
            Assert.DoesNotContain("empty result", outcome.Error!.Message);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_Check_ReportsEachStatus_WithoutWritingAnything()
    {
        string dir = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(dir, "openapi.json"), Spec);
            CatalystConfig generate = ConfigWith(new OpenApiClientConfig { Name = "Current", Source = "openapi.json", Output = "out" });
            await ConfiguredClientRunner.RunAsync(generate, dir, CancellationToken.None);

            string staleFile = Path.Combine(dir, "out", "Stale.ts");
            File.WriteAllText(staleFile, "// hand edited");

            CatalystConfig config = ConfigWith(
                new OpenApiClientConfig { Name = "Current", Source = "openapi.json", Output = "out" },
                new OpenApiClientConfig { Name = "Stale", Source = "openapi.json", Output = "out" },
                new OpenApiClientConfig { Name = "Absent", Source = "openapi.json", Output = "out" },
                new OpenApiClientConfig { Name = "Broken", Source = "gone.json", Output = "out" });

            List<ClientResult> results = await ConfiguredClientRunner.RunAsync(config, dir, CancellationToken.None, check: true);

            Assert.Equal(
                [ClientStatus.UpToDate, ClientStatus.Stale, ClientStatus.Missing, ClientStatus.Failed],
                results.Select(r => r.Status));
            Assert.Equal("// hand edited", File.ReadAllText(staleFile));
            Assert.False(File.Exists(Path.Combine(dir, "out", "Absent.ts")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
