namespace Catalyst.Tests.Services;

public class OpenApiParserTests
{
    [Fact]
    public void Parse_ReturnsSuccess_ForValidSpec()
    {
        string json = File.ReadAllText(TestPaths.SpecPath("enterprise"));

        Result<OpenApiParseResult> result = OpenApiParser.Parse(json);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value!.Document);
        Assert.NotNull(result.Value!.Diagnostic);
    }

    [Fact]
    public void Parse_ReturnsSuccess_WithDiagnostics_ForInvalidSpec()
    {
        string json = File.ReadAllText(TestPaths.FixturePath("openapi-invalid.json"));

        Result<OpenApiParseResult> result = OpenApiParser.Parse(json);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value!.Diagnostic.Errors);
    }

    [Fact]
    public void Parse_ReturnsError_ForEmptyContent()
    {
        Result<OpenApiParseResult> result = OpenApiParser.Parse(string.Empty);

        Assert.False(result.IsSuccess);
    }

    [Theory]
    [InlineData("openapi: 3.0.1\ninfo: {}", null, true)]
    [InlineData("{\"openapi\":\"3.0.1\"}", null, false)]
    [InlineData("  \n{\"openapi\":\"3.0.1\"}", "spec", false)]
    [InlineData("{\"openapi\":\"3.0.1\"}", "spec.yaml", true)]
    [InlineData("{\"openapi\":\"3.0.1\"}", "https://example.com/spec.YML?token=1", true)]
    [InlineData("openapi: 3.0.1", "spec.json", false)]
    public void IsYaml_DetectsTheFormatFromTheExtensionThenTheContent(string content, string? sourceName, bool expected)
    {
        Assert.Equal(expected, OpenApiParser.IsYaml(content, sourceName));
    }

    [Fact]
    public void Parse_KeepsNullableOnCompositions_ForYamlToo()
    {
        const string yaml = """
            openapi: 3.0.1
            info: { title: Yaml, version: "1" }
            paths: {}
            components:
              schemas:
                Pet: { type: object, properties: { name: { type: string } } }
                Owner:
                  type: object
                  properties:
                    pet:
                      nullable: true
                      allOf:
                        - $ref: '#/components/schemas/Pet'
            """;

        OpenApiDocument document = OpenApiParser.Parse(yaml).Value!.Document;
        string code = SpecLoader.CreateGenerator(document).Generate();

        Assert.Contains("pet?: Pet | null;", code);
    }

    [Fact]
    public void Parse_QuotesTheReadersErrors_WhenThereIsNoDocument()
    {
        Result<OpenApiParseResult> result = OpenApiParser.Parse("{ \"openapi\": ");

        Assert.False(result.IsSuccess);
        Assert.StartsWith("Failed to parse OpenAPI document: ", result.ErrorMessage);
        Assert.DoesNotContain("empty result", result.ErrorMessage);
    }
}
