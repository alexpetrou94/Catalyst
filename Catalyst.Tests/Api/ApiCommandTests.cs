using System.Text.Json;

namespace Catalyst.Tests.Api;

// The tests replace the static consoles and some change the working directory, so they run one at a time.
[Collection("AnsiConsole")]
public class ApiCommandTests
{
    private static readonly string EdgeCases = TestPaths.SpecPath("edge-cases");

    private static CommandRun Api(params string[] arguments) => CommandHarness.Run(["api", .. arguments]);

    private static JsonElement ParseJson(CommandRun run)
    {
        Assert.Equal(string.Empty, run.Error);
        return JsonDocument.Parse(run.Output).RootElement;
    }

    [Fact]
    public void List_PrintsOneLinePerOperation_Filtered_AndSorted()
    {
        CommandRun run = Api("list", "--source", EdgeCases, "--filter", "items id");

        Assert.Equal(0, run.ExitCode);
        string[] lines = run.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(2, lines.Length);
        Assert.Matches(@"^GET\s+/items/\{id\}\s+client\.get\.itemsId\s+- Get an item", lines[0]);
        Assert.Matches(@"^PUT\s+/items/\{id\}\s+client\.put\.itemsId$", lines[1]);
    }

    [Fact]
    public void List_Json_HasTheEnvelopeAndOneEntryPerMatch()
    {
        JsonElement root = ParseJson(Api("list", "--source", EdgeCases, "--method", "put", "--filter", "items", "--json"));

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("api list", root.GetProperty("command").GetString());
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal(0, root.GetProperty("exitCode").GetInt32());
        JsonElement operation = Assert.Single(root.GetProperty("operations").EnumerateArray());
        Assert.Equal("PUT", operation.GetProperty("method").GetString());
        Assert.Equal("/items/{id}", operation.GetProperty("path").GetString());
        Assert.Equal("client.put.itemsId", operation.GetProperty("call").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
    }

    [Fact]
    public void Show_PrintsTheCallParametersBodyResponseAndTypes()
    {
        CommandRun run = Api("show", "PUT /items/{id}", "--source", EdgeCases);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("PUT /items/{id}", run.Output);
        Assert.Contains("client.put.itemsId(id: string, body: Derived, dryRun?: boolean, headers?: Record<string, string>, signal?: AbortSignal): Promise<ApiResult<void>>", run.Output);
        Assert.Matches(@"dryRun\s+dryRun\s+query\s+optional\s+boolean", run.Output);
        Assert.Contains("Body: Derived (application/json, required)", run.Output);
        Assert.Contains("Response: void", run.Output);
        Assert.Contains("export type Derived = Base & { name?: string };", run.Output);
        Assert.True(run.Output.IndexOf("export interface Base", StringComparison.Ordinal) < run.Output.IndexOf("export type Derived", StringComparison.Ordinal));
    }

    [Fact]
    public void Show_NoTypes_OmitsTheDeclarations_AndAcceptsAnUnquotedSelector()
    {
        CommandRun run = Api("show", "PUT", "/items/{id}", "--source", EdgeCases, "--no-types");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Call:", run.Output);
        Assert.DoesNotContain("Types:", run.Output);
        Assert.DoesNotContain("export ", run.Output);
    }

    [Fact]
    public void Show_Json_CarriesTheOperationAndItsTypes()
    {
        JsonElement root = ParseJson(Api("show", "client.put.itemsId", "--source", EdgeCases, "--json"));

        JsonElement operation = root.GetProperty("operation");
        Assert.Equal("api show", root.GetProperty("command").GetString());
        Assert.Equal("client.put.itemsId", operation.GetProperty("call").GetString());
        Assert.Equal("id", operation.GetProperty("parameters")[0].GetProperty("name").GetString());
        Assert.Equal("path", operation.GetProperty("parameters")[0].GetProperty("in").GetString());
        Assert.Equal("Derived", operation.GetProperty("body").GetProperty("type").GetString());
        Assert.Equal(["Base", "Derived"], root.GetProperty("types").EnumerateArray().Select(t => t.GetProperty("name").GetString()));
    }

    [Fact]
    public void Show_ReportsNotFound_WithSuggestions_AndExitCodeOne()
    {
        CommandRun text = Api("show", "get.serch", "--source", EdgeCases);
        JsonElement root = ParseJson(Api("show", "get.serch", "--source", EdgeCases, "--json"));

        Assert.Equal(1, text.ExitCode);
        Assert.Contains("No operation matches", text.Error);
        Assert.Contains("client.get.search", text.Error);
        Assert.Equal(string.Empty, text.Output);
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(1, root.GetProperty("exitCode").GetInt32());
        Assert.Equal("OPERATION_NOT_FOUND", root.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("client.get.search", root.GetProperty("suggestions")[0].GetString());
    }

    [Fact]
    public void Schema_PrintsTheTypeAndItsDependencies_ByComponentName()
    {
        CommandRun all = Api("schema", "Item.Dto", "--source", EdgeCases);
        JsonElement alone = ParseJson(Api("schema", "Item_Dto", "--source", EdgeCases, "--depth", "0", "--json"));
        CommandRun missing = Api("schema", "Itm_Dto", "--source", EdgeCases);

        Assert.Equal(0, all.ExitCode);
        Assert.Contains("export interface Item_Dto", all.Output);
        Assert.Contains("export interface Base", all.Output);
        Assert.Contains("export type IntegerStatus = 0 | 1 | 2;", all.Output);
        Assert.Equal(["Item_Dto"], alone.GetProperty("types").EnumerateArray().Select(t => t.GetProperty("name").GetString()));
        Assert.Equal(1, missing.ExitCode);
        Assert.Contains("Item_Dto", missing.Error);
    }

    [Fact]
    public void Source_ComesFromTheOnlyConfiguredClient_OrTheNamedOne_OrIsAUsageError()
    {
        string dir = CreateTempDirectory();
        string previous = Directory.GetCurrentDirectory();
        try
        {
            File.Copy(EdgeCases, Path.Combine(dir, "edge.json"));
            File.Copy(TestPaths.SpecPath("minimal"), Path.Combine(dir, "minimal.json"));
            Directory.SetCurrentDirectory(dir);

            File.WriteAllText(Path.Combine(dir, "catalyst.config.json"), """
                { "gen": { "openapi": { "skipPaths": ["/internal/"], "clients": [
                  { "name": "Edge", "source": "edge.json", "output": "out" } ] } } }
                """);
            CommandRun single = Api("list");

            File.WriteAllText(Path.Combine(dir, "catalyst.config.json"), """
                { "gen": { "openapi": { "skipPaths": ["/internal/"], "clients": [
                  { "name": "Edge", "source": "edge.json", "output": "out" },
                  { "name": "Small", "source": "minimal.json", "output": "out" } ] } } }
                """);
            CommandRun ambiguous = Api("list");
            CommandRun named = Api("list", "--client", "small");
            CommandRun unknown = Api("list", "--client", "Nope", "--json");

            Assert.Equal(0, single.ExitCode);
            Assert.Contains("client.get.itemsId", single.Output);
            Assert.DoesNotContain("internalHealth", single.Output);

            Assert.Equal(2, ambiguous.ExitCode);
            Assert.Contains("Edge, Small", ambiguous.Error);

            Assert.Equal(0, named.ExitCode);
            Assert.Contains("client.get.widgets", named.Output);
            Assert.DoesNotContain("itemsId", named.Output);

            JsonElement root = ParseJson(unknown);
            Assert.Equal(2, unknown.ExitCode);
            Assert.Equal("USAGE", root.GetProperty("error").GetProperty("code").GetString());
            Assert.Contains("Edge, Small", root.GetProperty("error").GetProperty("message").GetString());
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void UsageErrors_InJson_UseTheApiEnvelope()
    {
        CommandRun run = Api("show", "--source", EdgeCases, "--nope", "--json");
        JsonElement root = ParseJson(run);

        Assert.Equal(2, run.ExitCode);
        Assert.Equal("api show", root.GetProperty("command").GetString());
        Assert.Equal("USAGE", root.GetProperty("error").GetProperty("code").GetString());
    }

    private static string CreateTempDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), "catalyst-api-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
