using Catalyst.Commands.Api;
using Catalyst.Tests.TypeScript;

namespace Catalyst.Tests.Api;

/// <summary>Selector resolution, suggestions and the type closure, against the real fixtures.</summary>
[Collection("TypeScript")]
public class ApiLookupTests
{
    private readonly TypeScriptToolchain _toolchain;

    public ApiLookupTests(TypeScriptToolchain toolchain)
    {
        _toolchain = toolchain;
    }

    private static ClientModel ModelOf(string spec)
    {
        TypeScriptGenerator generator = SpecLoader.CreateGenerator(SpecLoader.Load(spec));
        generator.Generate();
        return generator.Model;
    }

    [Theory]
    [InlineData("GET /items/{id}", "client.get.itemsId")]
    [InlineData("get /items/{whatever}", "client.get.itemsId")]
    [InlineData("PUT /items/{id}", "client.put.itemsId")]
    [InlineData("getSearch", "client.get.search")]
    [InlineData("GETSEARCH", "client.get.search")]
    [InlineData("get.search", "client.get.search")]
    [InlineData("client.get.search", "client.get.search")]
    [InlineData("PostcodeLookup", "client.post.PostcodeLookup")]
    [InlineData("post.PostcodeLookup", "client.post.PostcodeLookup")]
    public void FindOperation_ResolvesEverySelectorForm(string selector, string expectedCall)
    {
        OperationMatch match = ApiLookup.FindOperation(ModelOf("edge-cases"), selector);

        Assert.Equal(expectedCall, match.Operation?.Call);
    }

    [Theory]
    [InlineData("GET /albums/{id}", "client.get.anAlbum")]
    [InlineData("get-an-album", "client.get.anAlbum")]
    [InlineData("get.anAlbum", "client.get.anAlbum")]
    public void FindOperation_ResolvesSpotifyOperations(string selector, string expectedCall)
    {
        OperationMatch match = ApiLookup.FindOperation(ModelOf("spotify"), selector);

        Assert.Equal(expectedCall, match.Operation?.Call);
    }

    [Fact]
    public void FindOperation_ReportsAmbiguity_WithTheCandidates()
    {
        ApiOperation First(string path) => new("GET", path, "same", null, null, [], false, "get", "same", "same()", [], null, new ApiResponse("void", null), []);
        ClientModel model = new([First("/a/{x}"), First("/b/{y}")], []);

        OperationMatch byId = ApiLookup.FindOperation(model, "same");
        OperationMatch byShape = ApiLookup.FindOperation(new ClientModel([First("/a/{x}"), First("/a/{y}")], []), "GET /a/{z}");

        Assert.Equal("OPERATION_AMBIGUOUS", byId.ErrorCode);
        Assert.Equal(2, byId.Suggestions.Count);
        Assert.Equal("OPERATION_AMBIGUOUS", byShape.ErrorCode);
    }

    [Fact]
    public void FindOperation_SuggestsTheClosestOperations_WhenNothingMatches()
    {
        OperationMatch typo = ApiLookup.FindOperation(ModelOf("edge-cases"), "get.serch");
        OperationMatch spotify = ApiLookup.FindOperation(ModelOf("spotify"), "album");

        Assert.Equal("OPERATION_NOT_FOUND", typo.ErrorCode);
        Assert.Contains("client.get.search", typo.Suggestions[0]);
        Assert.Equal("OPERATION_NOT_FOUND", spotify.ErrorCode);
        Assert.InRange(spotify.Suggestions.Count, 1, 5);
        Assert.All(spotify.Suggestions, s => Assert.Contains("lbum", s, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FindType_AcceptsTheGeneratedNameOrTheComponentName()
    {
        ClientModel model = ModelOf("edge-cases");

        Assert.Equal("Item_Dto", ApiLookup.FindType(model, "Item_Dto")?.Name);
        Assert.Equal("Item_Dto", ApiLookup.FindType(model, "Item.Dto")?.Name);
        Assert.Equal("Item_Dto", ApiLookup.FindType(model, "item.dto")?.Name);
        Assert.Null(ApiLookup.FindType(model, "Nope"));
    }

    [Fact]
    public void TypeClosure_ListsDependenciesFirst_AndDepthStopsTheWalk()
    {
        ClientModel model = ModelOf("spotify");

        Assert.Equal(["AlbumObject"], ApiLookup.TypeClosure(model, ["AlbumObject"], depth: 0).Select(t => t.Name));
        Assert.Equal(
            ["AlbumBase", "ArtistObject", "PagingSimplifiedTrackObject", "AlbumObject"],
            ApiLookup.TypeClosure(model, ["AlbumObject"], depth: 1).Select(t => t.Name));
    }

    [Fact]
    public void TypeClosure_ForASpotifyOperation_HasExactlyTheExpectedTypes_AndTheirDeclarationsCompile()
    {
        ClientModel model = ModelOf("spotify");
        ApiOperation operation = ApiLookup.FindOperation(model, "GET /albums/{id}").Operation!;

        List<TypeDeclaration> closure = ApiLookup.TypeClosure(model, operation.ReferencedTypes);

        // AlbumObject is allOf-based; its closure pulls in the base type, artists and the track page.
        Assert.Equal(
            [
                "CopyrightObject", "ExternalIdObject", "ExternalUrlObject", "ImageObject", "AlbumRestrictionObject", "AlbumBase",
                "FollowersObject", "ArtistObject", "PagingObject", "SimplifiedArtistObject", "LinkedTrackObject",
                "TrackRestrictionObject", "SimplifiedTrackObject", "PagingSimplifiedTrackObject", "AlbumObject",
            ],
            closure.Select(t => t.Name));

        // Dependency order: every type comes after the types it depends on.
        for (int i = 0; i < closure.Count; i++)
        {
            Assert.All(closure[i].DependsOn, dependency => Assert.True(
                closure.FindIndex(t => t.Name == dependency) < i || dependency == closure[i].Name,
                $"{closure[i].Name} is printed before its dependency {dependency}"));
        }

        ToolResult result = _toolchain.Typecheck(string.Join("\n\n", closure.Select(t => t.Declaration)) + "\n");
        Assert.True(result.ExitCode == 0, "The printed declarations do not compile:" + Environment.NewLine + result.Output);
    }
}
