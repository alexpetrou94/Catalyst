using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace Catalyst.Tests.Services;

public class OpenApiTypeMapperTests
{
    [Fact]
    public void MapSchema_ReturnsString_ForStringType()
    {
        OpenApiSchema schema = new OpenApiSchema { Type = JsonSchemaType.String };
        Assert.Equal("string", Catalyst.Common.Services.OpenApiTypeMapper.MapSchema(schema));
    }

    [Fact]
    public void MapSchema_ReturnsNumber_ForIntegerType()
    {
        OpenApiSchema schema = new OpenApiSchema { Type = JsonSchemaType.Integer };
        Assert.Equal("number", Catalyst.Common.Services.OpenApiTypeMapper.MapSchema(schema));
    }

    [Fact]
    public void MapSchema_ReturnsBoolean_ForBooleanType()
    {
        OpenApiSchema schema = new OpenApiSchema { Type = JsonSchemaType.Boolean };
        Assert.Equal("boolean", Catalyst.Common.Services.OpenApiTypeMapper.MapSchema(schema));
    }

    [Fact]
    public void MapSchema_AppendsNullable_ForNullFlag()
    {
        OpenApiSchema schema = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null };
        Assert.Equal("string | null", Catalyst.Common.Services.OpenApiTypeMapper.MapSchema(schema));
    }

    [Fact]
    public void MapSchema_ReturnsArray_ForArrayType()
    {
        OpenApiSchema schema = new OpenApiSchema
        {
            Type = JsonSchemaType.Array,
            Items = new OpenApiSchema { Type = JsonSchemaType.String },
        };
        Assert.Equal("string[]", Catalyst.Common.Services.OpenApiTypeMapper.MapSchema(schema));
    }

    [Fact]
    public void MapSchema_ReturnsUnion_ForEnum()
    {
        OpenApiSchema schema = new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Enum = new List<JsonNode>
            {
                JsonNode.Parse("\"a\"")!,
                JsonNode.Parse("\"b\"")!,
            },
        };
        Assert.Equal("\"a\" | \"b\"", Catalyst.Common.Services.OpenApiTypeMapper.MapSchema(schema));
    }

    [Fact]
    public void MapSchema_ReturnsRecord_ForAdditionalPropertiesOnly()
    {
        OpenApiSchema schema = new OpenApiSchema
        {
            AdditionalProperties = new OpenApiSchema { Type = JsonSchemaType.String },
        };
        Assert.Equal("Record<string, string>", Catalyst.Common.Services.OpenApiTypeMapper.MapSchema(schema));
    }

    [Fact]
    public void MapSchema_ReturnsRefId_ForSchemaReference()
    {
        OpenApiSchemaReference schema = new OpenApiSchemaReference("Pet", new OpenApiDocument());
        Assert.Equal("Pet", Catalyst.Common.Services.OpenApiTypeMapper.MapSchema(schema));
    }
}
