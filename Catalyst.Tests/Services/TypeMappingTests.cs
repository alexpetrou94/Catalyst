namespace Catalyst.Tests.Services;

/// <summary>Schema JSON to TypeScript type string, through the generator's inline resolver.</summary>
public class TypeMappingTests
{
    private static string Resolve(string openApiVersion, string schemaJson)
    {
        string spec = $$"""
            {
              "openapi": "{{openApiVersion}}",
              "info": { "title": "Types", "version": "1" },
              "paths": {},
              "components": {
                "schemas": {
                  "Pet": { "type": "object", "properties": { "name": { "type": "string" } } },
                  "Status": { "type": "string", "enum": ["a", "b"] },
                  "Subject": {{schemaJson}}
                }
              }
            }
            """;

        OpenApiDocument document = SpecLoader.Parse(spec);
        return SpecLoader.CreateGenerator(document).ResolveInlineType(document.Components!.Schemas!["Subject"]);
    }

    [Theory]
    [InlineData("""{ "type": "string" }""", "string")]
    [InlineData("""{ "type": "string", "format": "date-time" }""", "string")]
    [InlineData("""{ "type": "integer" }""", "number")]
    [InlineData("""{ "type": "number" }""", "number")]
    [InlineData("""{ "type": "boolean" }""", "boolean")]
    [InlineData("""{ "type": "null" }""", "null")]
    [InlineData("""{}""", "any")]
    [InlineData("""{ "$ref": "#/components/schemas/Pet" }""", "Pet")]
    [InlineData("""{ "type": "array", "items": { "type": "string" } }""", "string[]")]
    [InlineData("""{ "type": "array" }""", "any[]")]
    [InlineData("""{ "type": "array", "items": { "$ref": "#/components/schemas/Pet" } }""", "Pet[]")]
    [InlineData("""{ "type": "array", "items": { "oneOf": [{ "type": "string" }, { "type": "integer" }] } }""", "(string | number)[]")]
    [InlineData("""{ "type": "object" }""", "Record<string, unknown>")]
    [InlineData("""{ "type": "object", "additionalProperties": { "type": "string" } }""", "Record<string, string>")]
    [InlineData("""{ "additionalProperties": { "type": "integer" } }""", "Record<string, number>")]
    [InlineData("""{ "type": "object", "additionalProperties": { "type": "object", "properties": { "a": { "type": "string" } } } }""", "Record<string, { a?: string }>")]
    [InlineData("""{ "type": "object", "required": ["x-trace"], "properties": { "x-trace": { "type": "string" }, "n": { "type": "number" } } }""", """{ "x-trace": string; n?: number }""")]
    public void Resolve_MapsBasicShapes(string schemaJson, string expected)
    {
        Assert.Equal(expected, Resolve("3.1.0", schemaJson));
    }

    [Theory]
    [InlineData("""{ "type": ["string", "null"] }""", "string | null")]
    [InlineData("""{ "type": ["string", "integer"] }""", "string | number")]
    [InlineData("""{ "type": ["array", "null"], "items": { "type": "string" } }""", "string[] | null")]
    [InlineData("""{ "type": "array", "items": { "type": ["string", "null"] } }""", "(string | null)[]")]
    [InlineData("""{ "oneOf": [{ "type": "null" }, { "$ref": "#/components/schemas/Pet" }] }""", "Pet | null")]
    [InlineData("""{ "oneOf": [{ "type": "string" }, { "type": "number" }, { "type": "null" }, { "type": "null" }] }""", "string | number | null")]
    [InlineData("""{ "oneOf": [{ "type": "string" }, { "type": "string" }] }""", "string")]
    [InlineData("""{ "anyOf": [{ "type": "string" }, { "type": "integer" }] }""", "string | number")]
    [InlineData("""{ "anyOf": [{ "$ref": "#/components/schemas/Pet" }, { "type": "null" }] }""", "Pet | null")]
    public void Resolve_MapsNullabilityAndUnions(string schemaJson, string expected)
    {
        Assert.Equal(expected, Resolve("3.1.0", schemaJson));
    }

    [Theory]
    [InlineData("""{ "allOf": [{ "$ref": "#/components/schemas/Pet" }] }""", "Pet")]
    [InlineData("""{ "allOf": [{ "$ref": "#/components/schemas/Pet" }, { "$ref": "#/components/schemas/Status" }] }""", "Pet & Status")]
    [InlineData("""{ "allOf": [{ "$ref": "#/components/schemas/Pet" }, { "type": "object", "properties": { "extra": { "type": "string" } } }] }""", "Pet & { extra?: string }")]
    [InlineData("""{ "allOf": [{ "$ref": "#/components/schemas/Pet" }], "properties": { "own": { "type": "integer" } } }""", "Pet & { own?: number }")]
    [InlineData("""{ "allOf": [{ "$ref": "#/components/schemas/Pet" }, { "oneOf": [{ "type": "string" }, { "type": "integer" }] }] }""", "Pet & (string | number)")]
    [InlineData("""{ "allOf": [{ "$ref": "#/components/schemas/Pet" }, { "type": "null" }] }""", "Pet | null")]
    public void Resolve_MapsAllOfToAnIntersection(string schemaJson, string expected)
    {
        Assert.Equal(expected, Resolve("3.1.0", schemaJson));
    }

    [Theory]
    [InlineData("""{ "type": "string", "nullable": true }""", "string | null")]
    [InlineData("""{ "nullable": true, "allOf": [{ "$ref": "#/components/schemas/Pet" }] }""", "Pet | null")]
    [InlineData("""{ "allOf": [{ "$ref": "#/components/schemas/Pet" }], "nullable": false }""", "Pet")]
    [InlineData("""{ "nullable": true, "oneOf": [{ "type": "string" }, { "type": "integer" }] }""", "string | number | null")]
    [InlineData("""{ "nullable": true, "anyOf": [{ "$ref": "#/components/schemas/Pet" }] }""", "Pet | null")]
    public void Resolve_KeepsOpenApi30NullableOnCompositions(string schemaJson, string expected)
    {
        Assert.Equal(expected, Resolve("3.0.1", schemaJson));
    }

    [Theory]
    [InlineData("""{ "type": "string", "enum": ["a", "b"] }""", "\"a\" | \"b\"")]
    [InlineData("""{ "type": "string", "enum": ["say \"hi\"", "back\\slash"] }""", "\"say \\\"hi\\\"\" | \"back\\\\slash\"")]
    [InlineData("""{ "type": "integer", "enum": [0, 1, 2] }""", "0 | 1 | 2")]
    [InlineData("""{ "type": "number", "enum": [-1, 1.5] }""", "-1 | 1.5")]
    [InlineData("""{ "enum": ["a", 1, true] }""", "\"a\" | 1 | true")]
    [InlineData("""{ "type": ["string", "null"], "enum": ["a", null] }""", "\"a\" | null")]
    public void Resolve_MapsEnumsToLiteralUnions(string schemaJson, string expected)
    {
        Assert.Equal(expected, Resolve("3.1.0", schemaJson));
    }
}
