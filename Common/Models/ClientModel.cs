using System.Text.Json.Serialization;

namespace Catalyst.Common.Models;

/// <summary>One parameter of a generated method (path or query), in the order of the generated signature.</summary>
internal sealed record ApiParameter(string Name, string WireName, string In, bool Required, string Type, string? Description);

internal sealed record ApiBody(string Type, string MediaType, bool Required);

internal sealed record ApiResponse(string Type, string? MediaType);

/// <summary>
/// One operation as the generated client exposes it: the HTTP side (method, path, ids, docs) and the
/// TypeScript side (verb group, method name, signature, parameter and body types).
/// </summary>
internal sealed record ApiOperation(
    string Method,
    string Path,
    string? OperationId,
    string? Summary,
    string? Description,
    List<string> Tags,
    bool Deprecated,
    string Verb,
    string MethodName,
    string Signature,
    List<ApiParameter> Parameters,
    ApiBody? Body,
    ApiResponse Response,
    List<string> ReferencedTypes)
{
    /// <summary>How the operation is called on the generated client, for example client.get.itemsId.</summary>
    public string Call => $"client.{Verb}.{MethodName}";
}

/// <summary>A named type of the generated file with its rendered TypeScript and the declared types it uses.</summary>
internal sealed record TypeDeclaration(string Name, string Kind, string? ComponentName, string Declaration, List<string> DependsOn)
{
    /// <summary>Interface properties (name as in the spec, TypeScript type, required); for comparing versions of a type.</summary>
    [JsonIgnore]
    public List<TypeMember> Members { get; init; } = [];

    /// <summary>The right-hand side of a type alias.</summary>
    [JsonIgnore]
    public string? AliasType { get; init; }

    /// <summary>The values of a string enum.</summary>
    [JsonIgnore]
    public List<string> Values { get; init; } = [];
}

internal sealed record TypeMember(string Name, string Type, bool Required);

/// <summary>What the generator knows about the client it renders: the operations and the declared types.</summary>
internal sealed record ClientModel(List<ApiOperation> Operations, List<TypeDeclaration> Types);
