using System.Text.Json.Nodes;
using Catalyst.Common.Models;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Microsoft.OpenApi.YamlReader;
using SharpYaml.Serialization;
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

    /// <summary>How many reader errors are quoted when a document cannot be read at all.</summary>
    private const int MaxReportedErrors = 3;

    /// <summary>
    /// Parses a JSON or YAML OpenAPI document. YAML is converted to JSON first, so both formats go
    /// through the same preprocessing and reader. <paramref name="sourceName"/> (a path or URL) is
    /// only used to detect the format; without a telling extension the content decides.
    /// </summary>
    public static Result<OpenApiParseResult> Parse(string content, string? sourceName = null)
    {
        try
        {
            string json = IsYaml(content, sourceName) ? ConvertYamlToJson(content) : content;
            ReadResult result = OpenApiModelFactory.Parse(PreserveNullableComposition(json), "json", new OpenApiReaderSettings());

            if (result.Document is null || result.Diagnostic is null)
            {
                return Result<OpenApiParseResult>.Error("Failed to parse OpenAPI document: " + DescribeFailure(result.Diagnostic));
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

    /// <summary>Quotes the reader's own errors, so a malformed document says what is wrong with it.</summary>
    private static string DescribeFailure(OpenApiDiagnostic? diagnostic)
    {
        List<OpenApiError> errors = diagnostic?.Errors?.ToList() ?? [];
        if (errors.Count == 0)
        {
            return "the reader produced an empty result.";
        }

        List<string> shown = errors
            .Take(MaxReportedErrors)
            .Select(e => string.IsNullOrEmpty(e.Pointer) ? e.Message : $"{e.Pointer}: {e.Message}")
            .ToList();
        string more = errors.Count > MaxReportedErrors ? $" (and {errors.Count - MaxReportedErrors} more)" : string.Empty;

        return string.Join("; ", shown) + more;
    }

    /// <summary>YAML by extension (.yaml/.yml), JSON by extension (.json), otherwise by the first character: JSON starts with "{".</summary>
    internal static bool IsYaml(string content, string? sourceName)
    {
        string? name = sourceName?.Split('?', '#')[0];
        string extension = string.IsNullOrEmpty(name) ? string.Empty : Path.GetExtension(name);

        if (extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase) || extension.Equals(".yml", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string trimmed = content.TrimStart('﻿').TrimStart();
        return trimmed.Length > 0 && trimmed[0] != '{';
    }

    private static string ConvertYamlToJson(string content)
    {
        YamlStream stream = new();
        stream.Load(new StringReader(content));

        if (stream.Documents.Count == 0)
        {
            throw new FormatException("the YAML document is empty.");
        }

        // The converter refuses documents whose aliases expand to an unreasonable number of nodes.
        return YamlConverter.ToJsonNode(stream.Documents[0]).ToJsonString();
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
