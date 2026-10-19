using System.Text.Json.Nodes;
using Catalyst.Common.Models;
using Microsoft.OpenApi.Reader;
using Auxil.Common;

namespace Catalyst.Common.Services;

internal static class OpenApiParser
{
    /// <summary>
    /// Extension the parser leaves on a schema that was marked "nullable: true" in an OpenAPI 3.0
    /// document without a "type" (the Swashbuckle style for a nullable $ref: nullable plus allOf).
    /// Microsoft.OpenApi only records nullability together with a type, so it would otherwise be lost.
    /// </summary>
    internal const string NullableExtension = "x-catalyst-nullable";

    public static Result<OpenApiParseResult> Parse(string content)
    {
        try
        {
            ReadResult result = OpenApiModelFactory.Parse(PreserveNullableComposition(content), "json", new OpenApiReaderSettings());

            if (result.Document is null || result.Diagnostic is null)
            {
                return Result<OpenApiParseResult>.Error("Failed to parse OpenAPI document: produced an empty result.");
            }

            return Result<OpenApiParseResult>.Ok(new OpenApiParseResult
            {
                Document = result.Document,
                Diagnostic = result.Diagnostic,
            });
        }
        catch (Exception ex)
        {
            return Result<OpenApiParseResult>.Error($"Failed to parse OpenAPI document: {ex.Message}");
        }
    }

    /// <summary>
    /// For OpenAPI 3.0 documents, marks every untyped allOf/anyOf/oneOf schema that says "nullable: true"
    /// with <see cref="NullableExtension"/>. Content that is not a 3.0 JSON document is returned as is.
    /// </summary>
    internal static string PreserveNullableComposition(string content)
    {
        try
        {
            JsonNode? root = JsonNode.Parse(content);
            if (root is not JsonObject rootObject
                || rootObject["openapi"]?.GetValue<string>() is not { } version
                || !version.StartsWith("3.0", StringComparison.Ordinal))
            {
                return content;
            }

            if (!MarkNullableComposition(root))
            {
                return content;
            }

            return root.ToJsonString();
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or ArgumentException or InvalidOperationException or FormatException)
        {
            // Not valid JSON: leave it to the OpenAPI reader to report.
            return content;
        }
    }

    private static bool MarkNullableComposition(JsonNode node)
    {
        bool changed = false;

        if (node is JsonObject obj)
        {
            if (obj["nullable"] is JsonValue { } nullable
                && nullable.TryGetValue(out bool isNullable)
                && isNullable
                && !obj.ContainsKey("type")
                && (obj.ContainsKey("allOf") || obj.ContainsKey("anyOf") || obj.ContainsKey("oneOf")))
            {
                obj[NullableExtension] = true;
                changed = true;
            }

            foreach (KeyValuePair<string, JsonNode?> child in obj.ToList())
            {
                if (child.Value != null)
                {
                    changed |= MarkNullableComposition(child.Value);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (JsonNode? child in array)
            {
                if (child != null)
                {
                    changed |= MarkNullableComposition(child);
                }
            }
        }

        return changed;
    }
}
