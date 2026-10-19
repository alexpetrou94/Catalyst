namespace Catalyst.Tests.Generation;

/// <summary>The one YAML behaviour test: the original YAML spec generates exactly what its JSON conversion does.</summary>
public class YamlSpecTests
{
    [Fact]
    public void Generate_FromYaml_EqualsTheSnapshotOfTheJsonVersion()
    {
        string yamlPath = TestPaths.SpecPath("museum", ".yaml");
        Result<OpenApiParseResult> parsed = OpenApiParser.Parse(File.ReadAllText(yamlPath), yamlPath);

        Assert.True(parsed.IsSuccess, parsed.ErrorMessage);

        string code = SpecLoader.CreateGenerator(parsed.Value!.Document).Generate();
        string snapshot = File.ReadAllText(TestPaths.SnapshotPath("museum"));

        Assert.Equal(Normalize(snapshot), Normalize(code));
    }

    private static string Normalize(string value) =>
        value.Replace("\r\n", "\n").Trim();
}
