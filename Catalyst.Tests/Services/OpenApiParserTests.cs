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
}
