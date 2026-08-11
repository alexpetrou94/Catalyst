namespace Catalyst.Tests.Services;

public class SourceResolverTests
{
    [Theory]
    [InlineData("https://api.example.com/openapi.json", SourceType.Url)]
    [InlineData("http://localhost/swagger.json", SourceType.Url)]
    [InlineData("./swagger.json", SourceType.FilePath)]
    [InlineData("C:\\temp\\spec.json", SourceType.FilePath)]
    internal void DetermineSourceType_ClassifiesCorrectly(string source, SourceType expected)
    {
        Assert.Equal(expected, SourceResolver.DetermineSourceType(source));
    }

    [Fact]
    public async Task Resolve_ReturnsError_ForMissingFile()
    {
        Result<SourceResolveResult> result = await SourceResolver.Resolve("./does-not-exist-12345.json", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    [Fact]
    public async Task Resolve_ReturnsContent_ForExistingFile()
    {
        string path = Path.Combine(Path.GetTempPath(), "catalyst-resolver-test.json");
        await File.WriteAllTextAsync(path, "{ \"hello\": \"world\" }");

        try
        {
            Result<SourceResolveResult> result = await SourceResolver.Resolve(path, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(SourceType.FilePath, result.Value!.Type);
            Assert.Contains("hello", result.Value!.Content);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
