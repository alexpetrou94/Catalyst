using System.Diagnostics;
using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Nodes;
using Catalyst.Commands.Mcp;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Catalyst.Tests.Mcp;

/// <summary>
/// Drives the real tool set through the SDK client over in-memory pipes. The payload of each tool is compared with what
/// the CLI prints with --json for the same inputs; the lookup and diff semantics themselves are tested with the api commands.
/// </summary>
// Some tests change the working directory (config discovery) and the CLI helper replaces the static consoles.
[Collection("AnsiConsole")]
public class McpServerTests
{
    private static readonly string EdgeCases = TestPaths.SpecPath("edge-cases");

    private sealed class Session : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _server;

        public McpClient Client { get; }

        private Session(ServiceProvider provider, Task server, McpClient client)
        {
            _provider = provider;
            _server = server;
            Client = client;
        }

        public static async Task<Session> StartAsync()
        {
            Pipe clientToServer = new();
            Pipe serverToClient = new();

            ServiceCollection services = new();
            services.AddLogging();
            services.AddCatalystServer().WithStreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream());
            ServiceProvider provider = services.BuildServiceProvider();

            Task server = provider.GetRequiredService<McpServer>().RunAsync();
            McpClient client = await McpClient.CreateAsync(new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()));
            return new Session(provider, server, client);
        }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            await _provider.DisposeAsync();
            _stop.Dispose();
        }
    }

    private static async Task<CallToolResult> Call(Session session, string tool, Dictionary<string, object?> arguments)
    {
        return await session.Client.CallToolAsync(tool, arguments);
    }

    private static JsonNode Structured(CallToolResult result) => JsonNode.Parse(result.StructuredContent!.Value.GetRawText())!;

    private static JsonNode Cli(params string[] arguments)
    {
        CommandRun run = CommandHarness.Run(arguments);
        Assert.Equal(string.Empty, run.Error);
        return JsonNode.Parse(run.Output)!;
    }

    private static string CreateTempDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), "catalyst-mcp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public async Task ToolsList_HasExactlyTheSixTools_EachWithAnInputSchema()
    {
        await using Session session = await Session.StartAsync();

        IList<McpClientTool> tools = await session.Client.ListToolsAsync();

        Assert.Equal(
            ["diff_spec", "generate_clients", "list_clients", "list_operations", "show_operation", "show_type"],
            tools.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.All(tools, tool =>
        {
            Assert.Equal(JsonValueKind.Object, tool.JsonSchema.ValueKind);
            Assert.Equal("object", tool.JsonSchema.GetProperty("type").GetString());
            Assert.False(string.IsNullOrWhiteSpace(tool.Description));
        });
        McpClientTool show = tools.Single(t => t.Name == "show_operation");
        Assert.Equal("operation", Assert.Single(show.JsonSchema.GetProperty("required").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task LookupTools_ReturnThePayloadOfTheMatchingCliCommand()
    {
        await using Session session = await Session.StartAsync();

        CallToolResult list = await Call(session, "list_operations", new() { ["source"] = EdgeCases, ["filter"] = "items id", ["method"] = "get" });
        CallToolResult show = await Call(session, "show_operation", new() { ["source"] = EdgeCases, ["operation"] = "PUT /items/{id}" });
        CallToolResult shortShow = await Call(session, "show_operation", new() { ["source"] = EdgeCases, ["operation"] = "PUT /items/{id}", ["includeTypes"] = false });
        CallToolResult type = await Call(session, "show_type", new() { ["source"] = EdgeCases, ["name"] = "Item.Dto", ["depth"] = 0 });

        Assert.True(JsonNode.DeepEquals(Cli("api", "list", "--source", EdgeCases, "--filter", "items id", "--method", "get", "--json"), Structured(list)));
        Assert.True(JsonNode.DeepEquals(Cli("api", "show", "PUT /items/{id}", "--source", EdgeCases, "--json"), Structured(show)));
        Assert.True(JsonNode.DeepEquals(Cli("api", "show", "PUT /items/{id}", "--source", EdgeCases, "--no-types", "--json"), Structured(shortShow)));
        Assert.True(JsonNode.DeepEquals(Cli("api", "schema", "Item.Dto", "--source", EdgeCases, "--depth", "0", "--json"), Structured(type)));
        Assert.Contains("client.put.itemsId(", ((TextContentBlock)show.Content[0]).Text);
        Assert.False(list.IsError);
    }

    [Fact]
    public async Task DiffSpec_ReportsChanges_WithoutFailing()
    {
        string dir = CreateTempDirectory();
        try
        {
            string newPath = Path.Combine(dir, "new.json");
            JsonObject spec = (JsonObject)JsonNode.Parse(File.ReadAllText(EdgeCases))!;
            ((JsonObject)spec["paths"]!).Remove("/ping");
            File.WriteAllText(newPath, spec.ToJsonString());
            await using Session session = await Session.StartAsync();

            CallToolResult diff = await Call(session, "diff_spec", new() { ["base"] = EdgeCases, ["source"] = newPath });

            Assert.False(diff.IsError);
            Assert.True(JsonNode.DeepEquals(Cli("api", "diff", "--base", EdgeCases, "--source", newPath, "--json"), Structured(diff)));
            Assert.Equal(1, Structured(diff)["summary"]!["breaking"]!.GetValue<int>());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task UnknownOperation_IsAToolError_WithTheCliCodeAndSuggestions()
    {
        await using Session session = await Session.StartAsync();

        CallToolResult result = await Call(session, "show_operation", new() { ["source"] = EdgeCases, ["operation"] = "get.serch" });

        Assert.True(result.IsError);
        JsonNode payload = Structured(result);
        Assert.Equal("OPERATION_NOT_FOUND", payload["error"]!["code"]!.GetValue<string>());
        Assert.Contains("client.get.search", payload["suggestions"]![0]!.GetValue<string>());
        Assert.Contains("Did you mean", ((TextContentBlock)result.Content[0]).Text);
    }

    [Fact]
    public async Task ConfiguredClientTools_ListGenerateAndCheck_OnlyTheDeclaredClients()
    {
        string dir = CreateTempDirectory();
        string previous = Directory.GetCurrentDirectory();
        try
        {
            File.Copy(TestPaths.SpecPath("minimal"), Path.Combine(dir, "minimal.json"));
            File.WriteAllText(Path.Combine(dir, "catalyst.config.json"), """
                { "gen": { "openapi": { "clients": [
                  { "name": "Small", "source": "minimal.json", "output": "out" },
                  { "name": "Other", "source": "minimal.json", "output": "other" } ] } } }
                """);
            Directory.SetCurrentDirectory(dir);
            await using Session session = await Session.StartAsync();

            JsonNode clients = Structured(await Call(session, "list_clients", []));
            CallToolResult missing = await Call(session, "generate_clients", new() { ["check"] = true });
            JsonNode cliCheck = Cli("gen", "openapi", "--check", "--json");
            bool checkWroteNothing = !Directory.Exists(Path.Combine(dir, "out")) && !Directory.Exists(Path.Combine(dir, "other"));
            CallToolResult unknown = await Call(session, "generate_clients", new() { ["client"] = "Nope" });
            CallToolResult generated = await Call(session, "generate_clients", new() { ["client"] = "Small" });

            Assert.Equal(["Small", "Other"], clients["clients"]!.AsArray().Select(c => c!["name"]!.GetValue<string>()));
            Assert.True(JsonNode.DeepEquals(cliCheck, Structured(missing)));
            Assert.Equal("missing", Structured(missing)["results"]![0]!["status"]!.GetValue<string>());
            Assert.True(checkWroteNothing);
            Assert.True(unknown.IsError);
            Assert.Equal("USAGE", Structured(unknown)["error"]!["code"]!.GetValue<string>());
            Assert.False(generated.IsError);
            Assert.Equal("written", Assert.Single(Structured(generated)["results"]!.AsArray())!["status"]!.GetValue<string>());
            Assert.True(File.Exists(Path.Combine(dir, "out", "Small.ts")));
            Assert.False(Directory.Exists(Path.Combine(dir, "other")));
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ListClients_IsEmpty_WithoutAConfig()
    {
        string dir = CreateTempDirectory();
        string previous = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(dir);
            await using Session session = await Session.StartAsync();

            CallToolResult result = await Call(session, "list_clients", []);

            Assert.False(result.IsError);
            Assert.Empty(Structured(result)["clients"]!.AsArray());
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// The published shape: a child process on real stdio. stdout must carry nothing but protocol messages, one JSON-RPC
    /// message per line; logs belong on stderr.
    /// </summary>
    [Fact]
    public async Task McpCommand_AnswersOverRealStdio_AndWritesOnlyProtocolMessagesToStdout()
    {
        string catalyst = Path.Combine(AppContext.BaseDirectory, "Catalyst.dll");
        ProcessStartInfo startInfo = new("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(catalyst);
        startInfo.ArgumentList.Add("mcp");

        using Process process = Process.Start(startInfo)!;
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(60));

        await process.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}""");
        await process.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
        await process.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
        await process.StandardInput.FlushAsync(timeout.Token);

        List<JsonNode> messages = [];
        while (messages.Count < 2)
        {
            string? line = await process.StandardOutput.ReadLineAsync(timeout.Token);
            Assert.NotNull(line);
            messages.Add(JsonNode.Parse(line)!);
        }

        process.StandardInput.Close();
        await process.WaitForExitAsync(timeout.Token);
        string rest = await process.StandardOutput.ReadToEndAsync(timeout.Token);

        Assert.Equal(string.Empty, rest.Trim());
        Assert.All(messages, message => Assert.Equal("2.0", message["jsonrpc"]!.GetValue<string>()));
        Assert.Equal("catalyst", messages[0]["result"]!["serverInfo"]!["name"]!.GetValue<string>());
        Assert.Equal(6, messages[1]["result"]!["tools"]!.AsArray().Count);
    }
}
