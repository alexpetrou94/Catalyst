namespace Catalyst.Tests;

/// <summary>Loads and parses spec fixtures the same way the CLI does.</summary>
internal static class SpecLoader
{
    public static OpenApiDocument Load(string name)
    {
        return Parse(File.ReadAllText(TestPaths.SpecPath(name)));
    }

    public static OpenApiDocument Parse(string json)
    {
        Result<OpenApiParseResult> result = OpenApiParser.Parse(json);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(result.ErrorMessage);
        }

        return result.Value!.Document;
    }

    /// <summary>Generates a client with the fixed timestamp and version the snapshots use.</summary>
    public static TypeScriptGenerator CreateGenerator(OpenApiDocument document, string? className = null, List<string>? skipPaths = null)
    {
        return new TypeScriptGenerator(
            document,
            className,
            skipPaths,
            generatorVersion: "0.0.0-test");
    }
}
