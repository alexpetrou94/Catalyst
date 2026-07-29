namespace Catalyst.Tests.Services;

public class TypeScriptGeneratorMinimalTests
{
    private static string Generate(string fixture, string? className = null)
    {
        string json = File.ReadAllText(fixture);
        ReadResult result = OpenApiModelFactory.Parse(json, "json", new OpenApiReaderSettings());
        if (result.Document is null)
        {
            throw new InvalidOperationException("Failed to parse fixture document.");
        }

        return new TypeScriptGenerator(result.Document, className).Generate();
    }

    [Fact]
    public void Generate_CoversParamsReturnsAndEdgeCases()
    {
        string code = Generate("Fixtures/openapi-minimal.json", "Minimal API");

        // Component schema + $ref resolution
        Assert.Contains("interface Widget", code);
        Assert.Contains("widgets(limit?: number, _class?: string, headers?: Record<string, string>): Promise<{ data: Widget[]; error: null } | { data: null; error: ProblemDetail }>", code);
        Assert.Contains("interface Get", code);

        // Request body ($ref) + created (201) response
        Assert.Contains("widgets(body: Widget, headers?: Record<string, string>): Promise<{ data: Widget; error: null } | { data: null; error: ProblemDetail }>", code);
        Assert.Contains("interface Post", code);

        // Path parameter
        Assert.Contains("widgetsId(id: string, headers?: Record<string, string>): Promise<{ data: Widget; error: null } | { data: null; error: ProblemDetail }>", code);

        // No-content (204) and no-response => void return
        Assert.Contains("widgetsId(id: string, headers?: Record<string, string>): Promise<{ data: void; error: null } | { data: null; error: ProblemDetail }>", code);
        Assert.Contains("void(headers?: Record<string, string>): Promise<{ data: void; error: null } | { data: null; error: ProblemDetail }>", code);

        // Primitive returns
        Assert.Contains("ping(headers?: Record<string, string>): Promise<{ data: string; error: null } | { data: null; error: ProblemDetail }>", code);
        Assert.Contains("count(headers?: Record<string, string>): Promise<{ data: number; error: null } | { data: null; error: ProblemDetail }>", code);
        Assert.Contains("flag(headers?: Record<string, string>): Promise<{ data: boolean; error: null } | { data: null; error: ProblemDetail }>", code);

        // Primitive array return
        Assert.Contains("items(headers?: Record<string, string>): Promise<{ data: string[]; error: null } | { data: null; error: ProblemDetail }>", code);

        // Inline request/response objects become named interfaces
        Assert.Contains("echo(body: PostEchoRequest, headers?: Record<string, string>): Promise<{ data: PostEchoResponse; error: null } | { data: null; error: ProblemDetail }>", code);
        Assert.Contains("interface PostEchoRequest", code);
        Assert.Contains("interface PostEchoResponse", code);

        // Inline nested object return becomes a named response interface
        Assert.Contains("nested(headers?: Record<string, string>): Promise<{ data: GetNestedResponse; error: null } | { data: null; error: ProblemDetail }>", code);
        Assert.Contains("interface GetNestedResponse", code);
        Assert.Contains("tags: string[]", code);
    }

    [Fact]
    public void Generate_HandlesOptionalAndReservedWordParams()
    {
        string code = Generate("Fixtures/openapi-minimal.json", "Minimal API");

        // Optional query param
        Assert.Contains("limit?: number", code);

        // Reserved word parameter uses a valid identifier binding (server name preserved as key)
        Assert.Contains("widgets(limit?: number, _class?: string, headers?: Record<string, string>): Promise<{ data: Widget[]; error: null } | { data: null; error: ProblemDetail }>", code);
    }

    [Fact]
    public void Generate_CamelCasesHyphenatedQueryParam()
    {
        string code = Generate("Fixtures/openapi-minimal.json", "Minimal API");

        // Required query param keeps its name; hyphenated param becomes camelCase and optional
        Assert.Contains("search(q: string, userId?: string, headers?: Record<string, string>): Promise<{ data: void; error: null } | { data: null; error: ProblemDetail }>", code);
    }

    [Fact]
    public void Generate_ClientInterfaceNamedFromSpecTitle()
    {
        string code = Generate("Fixtures/openapi-minimal.json", "Minimal API");

        Assert.Contains("export interface MinimalApiClient", code);
        Assert.Contains("export function createClient(options: ClientOptions = {}): MinimalApiClient", code);
        Assert.Contains("get: Get", code);
        Assert.Contains("post: Post", code);
        Assert.Contains("delete: Delete", code);
    }

    [Fact]
    public void Generate_ThrowsWhenNoServerUrlAndBaseUrlMissing()
    {
        string code = Generate("Fixtures/openapi-minimal.json", "Minimal API");

        Assert.Contains("throw new Error('baseUrl is required in ClientOptions')", code);
        Assert.Contains("const baseUrl = options.baseUrl;", code);
        Assert.DoesNotContain("http://localhost", code);
    }
}
