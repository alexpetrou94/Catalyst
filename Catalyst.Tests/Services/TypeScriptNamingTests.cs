namespace Catalyst.Tests.Services;

public class TypeScriptNamingTests
{
    [Theory]
    [InlineData("userId", "userId")]
    [InlineData("user-id", "user_id")]
    [InlineData("123abc", "_123abc")]
    [InlineData("class", "\"class\"")]
    [InlineData("interface", "\"interface\"")]
    [InlineData("with space", "with_space")]
    [InlineData("", "_")]
    [InlineData("_", "param")]
    public void SanitizeIdentifier_HandlesCases(string input, string expected)
    {
        Assert.Equal(expected, TypeScriptNaming.SanitizeIdentifier(input));
    }

    [Theory]
    [InlineData("class", "_class")]
    [InlineData("1st", "_1st")]
    [InlineData("a-b", "a_b")]
    public void SanitizeIdentifier_AsParameter_StaysAValidBinding(string input, string expected)
    {
        Assert.Equal(expected, TypeScriptNaming.SanitizeIdentifier(input, asParameter: true));
    }

    [Theory]
    [InlineData("getUser", "GetUser")]
    [InlineData("user-permissions-list", "UserPermissionsList")]
    [InlineData("", "Unknown")]
    [InlineData("API", "Api")]
    public void ToPascalCase_HandlesCases(string input, string expected)
    {
        Assert.Equal(expected, TypeScriptNaming.ToPascalCase(input));
    }

    [Theory]
    [InlineData("name", "name")]
    [InlineData("page-size", "\"page-size\"")]
    [InlineData("@odata.type", "\"@odata.type\"")]
    [InlineData("1st", "\"1st\"")]
    [InlineData("class", "class")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    public void QuoteKey_QuotesOnlyWhatIsNotAnIdentifier(string key, string expected)
    {
        Assert.Equal(expected, TypeScriptNaming.QuoteKey(key));
    }

    [Theory]
    [InlineData("Pet", "Pet")]
    [InlineData("Item.Dto", "Item_Dto")]
    [InlineData("Links-Self", "Links_Self")]
    [InlineData("A..B", "A_B")]
    [InlineData("Foo[]", "Foo")]
    [InlineData("1Thing", "_1Thing")]
    [InlineData("class", "_class")]
    [InlineData("Record", "Record_2")]
    [InlineData("string", "string_2")]
    [InlineData("***", "Schema")]
    public void BuildComponentNames_ProducesValidTypeIdentifiers(string key, string expected)
    {
        Dictionary<string, string> names = TypeScriptNaming.BuildComponentNames([key]);

        Assert.Equal(expected, names[key]);
    }

    [Fact]
    public void BuildComponentNames_KeepsValidKeysAndDisambiguatesTheRest()
    {
        // "Item.Dto" is listed first but must not take the name of the valid key "Item_Dto".
        Dictionary<string, string> names = TypeScriptNaming.BuildComponentNames(["Item.Dto", "Item_Dto", "Item-Dto"]);

        Assert.Equal("Item_Dto", names["Item_Dto"]);
        Assert.Equal("Item_Dto_2", names["Item.Dto"]);
        Assert.Equal("Item_Dto_3", names["Item-Dto"]);
        Assert.Equal(3, names.Values.Distinct().Count());
    }

    [Fact]
    public void BuildComponentNames_AvoidsNamesTheGeneratedFileDeclares()
    {
        Dictionary<string, string> names = TypeScriptNaming.BuildComponentNames(["ApiResult", "Get", "PetClient"], reserved: ["PetClient"]);

        Assert.Equal("ApiResult_2", names["ApiResult"]);
        Assert.Equal("Get_2", names["Get"]);
        Assert.Equal("PetClient_2", names["PetClient"]);
    }

    [Theory]
    [InlineData("GetUser", "get", false, "user")]
    [InlineData("PostcodeLookup", "post", false, "PostcodeLookup")]
    [InlineData("Getaway", "get", false, "Getaway")]
    [InlineData("getMedia", "get", true, "media")]
    [InlineData("getlowercase", "get", true, "lowercase")]
    [InlineData("get1abc", "get", true, "get1abc")]
    [InlineData("Get", "get", false, "Get")]
    public void StripHttpVerbPrefix_OnlyStripsAtAWordBoundary(string name, string verb, bool pathDerived, string expected)
    {
        Assert.Equal(expected, TypeScriptNaming.StripHttpVerbPrefix(name, verb, pathDerived));
    }
}
