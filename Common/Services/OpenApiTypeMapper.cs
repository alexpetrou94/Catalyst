using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace Catalyst.Common.Services;

internal static class OpenApiTypeMapper
{
    public static string MapSchema(IOpenApiSchema schema)
    {
        if (schema is OpenApiSchemaReference schemaRef)
        {
            return schemaRef.Reference.Id ?? "any";
        }

        if (schema.Enum?.Count > 0)
        {
            return MapEnum(schema);
        }

        JsonSchemaType? type = schema.Type;
        JsonSchemaType effectiveType = (type ?? 0) & ~JsonSchemaType.Null;
        bool isNullable = type?.HasFlag(JsonSchemaType.Null) == true;

        if (effectiveType.HasFlag(JsonSchemaType.Array))
        {
            if (schema.Items != null)
            {
                string itemType = MapSchema(schema.Items);
                return isNullable ? itemType + "[] | null" : itemType + "[]";
            }

            return isNullable ? "any[] | null" : "any[]";
        }

        if (effectiveType.HasFlag(JsonSchemaType.Object) || schema.Properties?.Count > 0 || schema.AdditionalProperties != null)
        {
            string objType = MapObjectSchema(schema);
            return isNullable ? objType + " | null" : objType;
        }

        string primitive = MapPrimitiveType(effectiveType);
        return isNullable ? primitive + " | null" : primitive;
    }

    private static string MapPrimitiveType(JsonSchemaType type)
    {
        if (type.HasFlag(JsonSchemaType.String))
        {
            return "string";
        }

        if (type.HasFlag(JsonSchemaType.Integer) || type.HasFlag(JsonSchemaType.Number))
        {
            return "number";
        }

        if (type.HasFlag(JsonSchemaType.Boolean))
        {
            return "boolean";
        }

        return "any";
    }

    private static string MapEnum(IOpenApiSchema schema)
    {
        List<string> values = [];

        if (schema.Enum is null)
        {
            return "any";
        }

        foreach (JsonNode item in schema.Enum)
        {
            if (item is JsonValue jsonValue)
            {
                if (jsonValue.TryGetValue(out string? str))
                {
                    values.Add($"\"{str}\"");
                }
                else if (jsonValue.TryGetValue(out int intValue))
                {
                    values.Add(intValue.ToString());
                }
                else if (jsonValue.TryGetValue(out bool boolValue))
                {
                    values.Add(boolValue ? "true" : "false");
                }
                else
                {
                    values.Add("any");
                }
            }
            else
            {
                values.Add("any");
            }
        }

        string union = string.Join(" | ", values);

        bool isNullable = schema.Type?.HasFlag(JsonSchemaType.Null) == true;
        return isNullable ? union + " | null" : union;
    }

    private static string MapObjectSchema(IOpenApiSchema schema)
    {
        if (schema.Properties == null || schema.Properties.Count == 0)
        {
            if (schema.AdditionalProperties != null)
            {
                string valueType = MapSchema(schema.AdditionalProperties);
                return $"Record<string, {valueType}>";
            }

            return "Record<string, any>";
        }

        List<string> properties = [];

        foreach (KeyValuePair<string, IOpenApiSchema> property in schema.Properties)
        {
            bool isRequired = schema.Required?.Contains(property.Key) ?? false;
            string optional = isRequired ? "" : "?";
            string propertyType = MapSchema(property.Value);
            properties.Add($"{property.Key}{optional}: {propertyType}");
        }

        return "{ " + string.Join("; ", properties) + " }";
    }
}
