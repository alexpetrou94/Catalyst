using System.Text.Json.Nodes;
using Catalyst.Commands.Api;

namespace Catalyst.Tests.Api;

/// <summary>
/// The comparison of two client models, one row per kind of change. Each row starts from the same small spec,
/// applies a change to a copy, and expects exactly the listed changes (severity:KIND).
/// </summary>
public class ApiDiffTests
{
    public sealed record Row(string Name, Action<JsonObject> Setup, Action<JsonObject> Change, params string[] Expected)
    {
        public override string ToString() => Name;
    }

    private static JsonObject Spec()
    {
        return (JsonObject)JsonNode.Parse("""
            {
              "openapi": "3.0.3",
              "info": { "title": "Diff", "version": "1" },
              "paths": {},
              "components": { "schemas": {} }
            }
            """)!;
    }

    private static JsonObject Paths(JsonObject root) => (JsonObject)root["paths"]!;

    private static JsonObject Schemas(JsonObject root) => (JsonObject)root["components"]!["schemas"]!;

    private static JsonObject Op(JsonObject root, string path = "/t", string method = "get") => (JsonObject)Paths(root)[path]![method]!;

    private static JsonArray Parameters(JsonObject root) => (JsonArray)Op(root)["parameters"]!;

    private static JsonObject Json(string json) => (JsonObject)JsonNode.Parse(json)!;

    private static void AddOperation(JsonObject root, string parameters = "[]", string method = "get", string? body = null, string response = """{ "type": "string" }""", string path = "/t", string operationId = "doIt")
    {
        string requestBody = body == null ? string.Empty : $$""" "requestBody": { "required": true, "content": { {{body}} } }, """;
        Paths(root)[path] = Json($$"""
            { "{{method}}": { "operationId": "{{operationId}}", "parameters": {{parameters}}, {{requestBody}}
              "responses": { "200": { "description": "ok", "content": { "application/json": { "schema": {{response}} } } } } } }
            """);
    }

    private static string Query(string name, bool required = false, string type = "string") =>
        $$"""{ "name": "{{name}}", "in": "query", "required": {{(required ? "true" : "false")}}, "schema": { "type": "{{type}}" } }""";

    private static string JsonBody(string schema) => $$""" "application/json": { "schema": {{schema}} } """;

    private static void AddSchema(JsonObject root, string name, string schema) => Schemas(root)[name] = Json(schema);

    private const string Obj = """{ "type": "object", "required": ["a"], "properties": { "a": { "type": "string" }, "b": { "type": "string" } } }""";

    public static IEnumerable<object[]> Rows()
    {
        Action<JsonObject> none = _ => { };
        Row[] rows =
        [
            new("operation removed", r => AddOperation(r), r => Paths(r).Remove("/t"), "breaking:OPERATION_REMOVED"),
            new("operation added", none, r => AddOperation(r), "non-breaking:OPERATION_ADDED"),
            new("operation moved, call unchanged", r => AddOperation(r), r => { Paths(r).Remove("/t"); AddOperation(r, path: "/u"); }, "non-breaking:OPERATION_MOVED"),
            new("generated call changed", r => AddOperation(r), r => Op(r)["operationId"] = "other", "breaking:OPERATION_CALL_CHANGED"),
            new("parameter added required", r => AddOperation(r, $"[{Query("a")}]"), r => Parameters(r).Add(Json(Query("b", required: true))), "breaking:PARAMETER_ADDED_REQUIRED"),
            new("parameter added optional", r => AddOperation(r, $"[{Query("a")}]"), r => Parameters(r).Add(Json(Query("b"))), "non-breaking:PARAMETER_ADDED_OPTIONAL"),
            new("parameter removed", r => AddOperation(r, $"[{Query("a")}]"), r => Parameters(r).Clear(), "breaking:PARAMETER_REMOVED"),
            new("parameter became required", r => AddOperation(r, $"[{Query("a")}]"), r => Op(r)["parameters"] = new JsonArray(Json(Query("a", required: true))), "breaking:PARAMETER_BECAME_REQUIRED"),
            new("parameter became optional", r => AddOperation(r, $"[{Query("a", required: true)}]"), r => Op(r)["parameters"] = new JsonArray(Json(Query("a"))), "non-breaking:PARAMETER_BECAME_OPTIONAL"),
            new("parameter type changed", r => AddOperation(r, $"[{Query("a")}]"), r => Op(r)["parameters"] = new JsonArray(Json(Query("a", type: "integer"))), "breaking:PARAMETER_TYPE_CHANGED"),
            new("parameters swapped", r => AddOperation(r, $"[{Query("a")}, {Query("b")}]"), r => Op(r)["parameters"] = new JsonArray(Json(Query("b")), Json(Query("a"))), "breaking:PARAMETER_MOVED", "breaking:PARAMETER_MOVED"),
            new("body added", r => AddOperation(r, method: "post"), r => AddOperation(r, method: "post", body: JsonBody("""{ "type": "string" }""")), "breaking:BODY_ADDED"),
            new("body removed", r => AddOperation(r, method: "post", body: JsonBody("""{ "type": "string" }""")), r => AddOperation(r, method: "post"), "breaking:BODY_REMOVED"),
            new("body type changed", r => AddOperation(r, method: "post", body: JsonBody("""{ "type": "string" }""")), r => AddOperation(r, method: "post", body: JsonBody("""{ "type": "integer" }""")), "breaking:BODY_TYPE_CHANGED"),
            new("body media type changed", r => AddOperation(r, method: "post", body: JsonBody("""{ "type": "string" }""")), r => AddOperation(r, method: "post", body: """ "text/plain": { "schema": { "type": "string" } } """), "non-breaking:BODY_MEDIA_TYPE_CHANGED"),
            new("response type changed", r => AddOperation(r), r => AddOperation(r, response: """{ "type": "integer" }"""), "breaking:RESPONSE_TYPE_CHANGED"),
            new("type removed", r => AddSchema(r, "T", Obj), r => Schemas(r).Remove("T"), "breaking:TYPE_REMOVED"),
            new("type added", none, r => AddSchema(r, "T", Obj), "non-breaking:TYPE_ADDED"),
            new("type kind changed", r => AddSchema(r, "T", Obj), r => AddSchema(r, "T", """{ "type": "string" }"""), "breaking:TYPE_KIND_CHANGED"),
            new("property removed", r => AddSchema(r, "T", Obj), r => ((JsonObject)Schemas(r)["T"]!["properties"]!).Remove("b"), "breaking:TYPE_PROPERTY_REMOVED"),
            new("property added required", r => AddSchema(r, "T", Obj), r => { Properties(r)["c"] = Json("""{ "type": "string" }"""); ((JsonArray)Schemas(r)["T"]!["required"]!).Add("c"); }, "breaking:TYPE_PROPERTY_ADDED_REQUIRED"),
            new("property added optional", r => AddSchema(r, "T", Obj), r => Properties(r)["c"] = Json("""{ "type": "string" }"""), "non-breaking:TYPE_PROPERTY_ADDED_OPTIONAL"),
            new("property type changed", r => AddSchema(r, "T", Obj), r => Properties(r)["b"] = Json("""{ "type": "integer" }"""), "breaking:TYPE_PROPERTY_TYPE_CHANGED"),
            new("property became required", r => AddSchema(r, "T", Obj), r => ((JsonArray)Schemas(r)["T"]!["required"]!).Add("b"), "breaking:TYPE_PROPERTY_BECAME_REQUIRED"),
            new("property became optional", r => AddSchema(r, "T", Obj), r => ((JsonArray)Schemas(r)["T"]!["required"]!).Clear(), "breaking:TYPE_PROPERTY_BECAME_OPTIONAL"),
            new("property literal member added", r => AddSchema(r, "T", LiteralProperty("[\"x\"]")), r => AddSchema(r, "T", LiteralProperty("[\"x\", \"y\"]")), "non-breaking:TYPE_MEMBER_ADDED"),
            new("property literal member removed", r => AddSchema(r, "T", LiteralProperty("[\"x\", \"y\"]")), r => AddSchema(r, "T", LiteralProperty("[\"x\"]")), "breaking:TYPE_MEMBER_REMOVED"),
            new("enum member added", r => AddSchema(r, "T", """{ "type": "string", "enum": ["x"] }"""), r => AddSchema(r, "T", """{ "type": "string", "enum": ["x", "y"] }"""), "non-breaking:TYPE_MEMBER_ADDED"),
            new("enum member removed", r => AddSchema(r, "T", """{ "type": "string", "enum": ["x", "y"] }"""), r => AddSchema(r, "T", """{ "type": "string", "enum": ["x"] }"""), "breaking:TYPE_MEMBER_REMOVED"),
            new("numeric union member added", r => AddSchema(r, "T", """{ "type": "integer", "enum": [0, 1] }"""), r => AddSchema(r, "T", """{ "type": "integer", "enum": [0, 1, 2] }"""), "non-breaking:TYPE_MEMBER_ADDED"),
            new("alias changed", r => AddSchema(r, "T", """{ "type": "string" }"""), r => AddSchema(r, "T", """{ "type": "integer" }"""), "breaking:TYPE_ALIAS_CHANGED"),
            new("enum became union", r => AddSchema(r, "T", """{ "type": "string", "enum": ["x"] }"""), r => AddSchema(r, "T", """{ "type": "integer", "enum": [0] }"""), "breaking:TYPE_KIND_CHANGED"),
        ];

        return rows.Select(row => new object[] { row });
    }

    private static string LiteralProperty(string values) =>
        $$"""{ "type": "object", "properties": { "p": { "type": "string", "enum": {{values}} } } }""";

    private static JsonObject Properties(JsonObject root) => (JsonObject)Schemas(root)["T"]!["properties"]!;

    private static List<ApiChange> Diff(JsonObject before, JsonObject after)
    {
        return ApiDiff.Compare(Model(before), Model(after));
    }

    private static ClientModel Model(JsonObject spec)
    {
        TypeScriptGenerator generator = SpecLoader.CreateGenerator(SpecLoader.Parse(spec.ToJsonString()));
        generator.Generate();
        return generator.Model;
    }

    private static string[] Describe(IEnumerable<ApiChange> changes) =>
        changes.Select(c => $"{c.Severity}:{c.Kind}").OrderBy(s => s, StringComparer.Ordinal).ToArray();

    [Theory]
    [MemberData(nameof(Rows))]
    public void Compare_ReportsExactlyTheChange(Row row)
    {
        JsonObject before = Spec();
        row.Setup(before);
        JsonObject after = (JsonObject)JsonNode.Parse(before.ToJsonString())!;
        row.Change(after);

        Assert.Equal(row.Expected.OrderBy(s => s, StringComparer.Ordinal), Describe(Diff(before, after)));
    }

    [Fact]
    public void Compare_FindsNoChanges_WhenASpecIsComparedWithItself()
    {
        ClientModel petstore = ModelOf(SpecLoader.Load("petstore"));

        Assert.Empty(ApiDiff.Compare(petstore, petstore));
        Assert.Empty(ApiDiff.Compare(ModelOf(SpecLoader.Load("petstore")), petstore));
    }

    [Fact]
    public void Compare_ReportsARealisticMultiChangeDiff_OnSpotify()
    {
        JsonObject before = (JsonObject)JsonNode.Parse(File.ReadAllText(TestPaths.SpecPath("spotify")))!;
        JsonObject after = (JsonObject)JsonNode.Parse(before.ToJsonString())!;

        // A removed operation, a new required query parameter, a removed parameter, a removed and a retyped property.
        ((JsonObject)Paths(after)["/me/albums"]!).Remove("delete");
        ((JsonArray)Op(after, "/albums/{id}")["parameters"]!).Add(Json(Query("includeCopyrights", required: true, type: "boolean")));
        Schemas(after)["ImageObject"]!["properties"]!.AsObject().Remove("url");
        Schemas(after)["CopyrightObject"]!["properties"]!["text"] = Json("""{ "type": "integer" }""");

        List<ApiChange> changes = ApiDiff.Compare(ModelOf(SpecLoader.Parse(before.ToJsonString())), ModelOf(SpecLoader.Parse(after.ToJsonString())));

        Assert.Equal(
            [
                "breaking:OPERATION_REMOVED",
                "breaking:PARAMETER_ADDED_REQUIRED",
                "breaking:TYPE_PROPERTY_REMOVED",
                "breaking:TYPE_PROPERTY_TYPE_CHANGED",
                "breaking:TYPE_REMOVED",
            ],
            Describe(changes));
        Assert.Contains(changes, c => c.Kind == "TYPE_PROPERTY_REMOVED" && c.Type == "ImageObject" && c.Member == "url");
        Assert.Contains(changes, c => c.Kind == "PARAMETER_ADDED_REQUIRED" && c.Operation!.Call == "client.get.anAlbum");
        Assert.Contains(changes, c => c.Kind == "TYPE_REMOVED" && c.Type == "RemoveAlbumsUserRequest");
    }

    private static ClientModel ModelOf(OpenApiDocument document)
    {
        TypeScriptGenerator generator = SpecLoader.CreateGenerator(document);
        generator.Generate();
        return generator.Model;
    }
}
