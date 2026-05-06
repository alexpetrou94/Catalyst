using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.OpenApi;
using UPhoricLibrary.CodeGeneration.Common;
using UPhoricLibrary.CodeGeneration.TypeScript;
using UPhoricLibrary.CodeGeneration.TypeScript.Builders;

namespace Catalyst.Common.Services;

internal sealed class TypeScriptGenerator
{
    private readonly OpenApiDocument _document;
    private readonly string _className;
    private readonly HashSet<string> _generatedTypeNames;

    public TypeScriptGenerator(OpenApiDocument document, string? className)
    {
        _document = document;
        _className = className ?? document.Info?.Title ?? "ApiClient";
        _generatedTypeNames = new HashSet<string>();
    }

    public string Generate()
    {
        List<OperationInfo> operations = ExtractOperations();

        var ts = new TypeScriptBuilder();
        FileBuilder file = ts.File("api.ts");

        BuildHeader(file);
        BuildClientOptionsInterface(file);
        BuildPathsInterface(file);
        BuildSchemaInterfaces(file);
        BuildOperationTypeInterfaces(file, operations);
        BuildApiClientInterface(file, operations);
        BuildFactoryFunction(file, operations);

        file.EndFile();
        return ts.ToString();
    }

    private void BuildHeader(FileBuilder file)
    {
        file.Comment($" Generated from {_className}");
        file.Comment(" Requires TypeScript target ES2015 or higher, or include ES2015 in lib");
        file.BlankLine();
    }

    private static void BuildClientOptionsInterface(FileBuilder file)
    {
        file.Interface("ClientOptions")
            .Export()
            .Property("baseUrl", "string")
                .Optional()
                .EndInterfaceProperty()
            .Property("credentials", "RequestCredentials")
                .Optional()
                .EndInterfaceProperty()
            .Property("headers", "Record<string, string>")
                .Optional()
                .EndInterfaceProperty()
            .EndInterface();
    }

    private static void BuildPathsInterface(FileBuilder file)
    {
        file.Interface("Paths")
            .Export()
            .EndInterface();
    }

    private void BuildSchemaInterfaces(FileBuilder file)
    {
        var schemas = _document.Components?.Schemas;
        if (schemas == null || schemas.Count == 0)
        {
            return;
        }

        foreach (var schemaEntry in schemas)
        {
            BuildInterfaceFromSchema(file, schemaEntry.Key, schemaEntry.Value);
        }
    }

    private void BuildInterfaceFromSchema(FileBuilder file, string name, IOpenApiSchema schema)
    {
        if (_generatedTypeNames.Contains(name))
        {
            return;
        }

        _generatedTypeNames.Add(name);

        InterfaceBuilder interfaceBuilder = file.Interface(name).Export();

        if (!string.IsNullOrWhiteSpace(schema.Description))
        {
            interfaceBuilder.WithJSDoc(js => js.Description(schema.Description));
        }

        if (schema.Properties != null)
        {
            foreach (var property in schema.Properties)
            {
                bool isRequired = schema.Required?.Contains(property.Key) ?? false;
                BuildInterfaceProperty(interfaceBuilder, property.Key, property.Value, isRequired);
            }
        }

        interfaceBuilder.EndInterface();
    }

    private static void BuildInterfaceProperty(InterfaceBuilder interfaceBuilder, string name, IOpenApiSchema schema, bool isRequired)
    {
        PropertyBuilder propertyBuilder = interfaceBuilder.Property(name, OpenApiTypeMapper.MapSchema(schema));

        if (!isRequired)
        {
            propertyBuilder.Optional();
        }

        propertyBuilder.EndInterfaceProperty();
    }

    private void BuildOperationTypeInterfaces(FileBuilder file, List<OperationInfo> operations)
    {
        foreach (OperationInfo operation in operations)
        {
            if (operation.RequestBodySchema != null)
            {
                operation.RequestBodyType = ResolveAndGenerateType(file, operation.Name + "Request", operation.RequestBodySchema);
            }

            if (operation.ResponseSchema != null)
            {
                operation.ReturnType = ResolveAndGenerateType(file, operation.Name + "Response", operation.ResponseSchema);
            }
        }
    }

    private string ResolveAndGenerateType(FileBuilder file, string suggestedName, IOpenApiSchema schema)
    {
        if (schema is OpenApiSchemaReference schemaRef)
        {
            return schemaRef.Reference.Id;
        }

        string pascalName = ToPascalCase(suggestedName);

        JsonSchemaType effectiveType = (schema.Type ?? 0) & ~JsonSchemaType.Null;
        if (schema.Items != null || effectiveType.HasFlag(JsonSchemaType.Array))
        {
            string itemType = schema.Items != null
                ? OpenApiTypeMapper.MapSchema(schema.Items)
                : "any";
            return itemType + "[]";
        }

        if (schema.Properties?.Count > 0)
        {
            BuildInterfaceFromSchema(file, pascalName, schema);
            return pascalName;
        }

        return OpenApiTypeMapper.MapSchema(schema);
    }

    private void BuildApiClientInterface(FileBuilder file, List<OperationInfo> operations)
    {
        string interfaceName = SanitizeIdentifier(ToPascalCase(_className)) + "Client";
        InterfaceBuilder clientInterface = file.Interface(interfaceName).Export();

        foreach (OperationInfo operation in operations)
        {
            MethodBuilder method = clientInterface.Method(operation.Name);

            if (!string.IsNullOrWhiteSpace(operation.Summary))
            {
                method.WithJSDoc(js => js.Description(operation.Summary));
            }

            foreach (ParameterInfo param in operation.PathParams)
            {
                method.Parameter(param.Name, param.Type).EndMethodParameter();
            }

            foreach (ParameterInfo param in operation.QueryParams)
            {
                method.Parameter(param.Name, param.Type).EndMethodParameter();
            }

            if (operation.RequestBodyType != null)
            {
                method.Parameter("body", operation.RequestBodyType).EndMethodParameter();
            }

            method.Returns("Promise<" + operation.ReturnType + ">").EndMethod();
        }

        clientInterface.EndInterface();
    }

    private void BuildFactoryFunction(FileBuilder file, List<OperationInfo> operations)
    {
        string interfaceName = SanitizeIdentifier(ToPascalCase(_className)) + "Client";
        string defaultBaseUrl = GetDefaultBaseUrl();

        file.Function("createClient")
            .Export()
            .Generic("T", g => g.Extends("Paths"))
            .Parameter("options", "ClientOptions")
                .DefaultValue("{}")
                .EndFunctionParameter()
            .Returns(interfaceName)
            .Body(b =>
            {
                b.Line($"const baseUrl = options.baseUrl || '{defaultBaseUrl}';");
                b.Line("const credentials = options.credentials;");
                b.Line("const headers = options.headers || {};");
                b.BlankLine();

                b.Line("async function request(path: string, method: string, init?: any): Promise<any> {");
                b.Indent();
                b.Line("let url = baseUrl + path;");
                b.Line("if (init?.params) {");
                b.Indent();
                b.Line("const search = new URLSearchParams(init.params);");
                b.Line("url += '?' + search.toString();");
                b.Dedent();
                b.Line("}");
                b.Line("const response = await fetch(url, {");
                b.Indent();
                b.Line("method,");
                b.Line("credentials,");
                b.Line("headers: { ...headers, ...init?.headers },");
                b.Line("body: init?.body ? JSON.stringify(init.body) : undefined,");
                b.Dedent();
                b.Line("});");
                b.Line("if (!response.ok) {");
                b.Indent();
                b.Line("throw new Error(`HTTP ${response.status}`);");
                b.Dedent();
                b.Line("}");
                b.Line("return response.json();");
                b.Dedent();
                b.Line("}");
                b.BlankLine();

                b.Line("return {");
                b.Indent();

                for (int i = 0; i < operations.Count; i++)
                {
                    OperationInfo operation = operations[i];
                    WriteFactoryMethodBody(b, operation);

                    if (i < operations.Count - 1)
                    {
                        b.BlankLine();
                    }
                }

                b.Dedent();
                b.Line("};");
            })
            .EndFunction();
    }

    private static void WriteFactoryMethodBody(BodyBuilder b, OperationInfo operation)
    {
        var signatureParams = new List<string>();

        foreach (ParameterInfo param in operation.PathParams)
        {
            signatureParams.Add($"{param.Name}: {param.Type}");
        }

        foreach (ParameterInfo param in operation.QueryParams)
        {
            signatureParams.Add($"{param.Name}: {param.Type}");
        }

        if (operation.RequestBodyType != null)
        {
            signatureParams.Add($"body: {operation.RequestBodyType}");
        }

        string paramsList = string.Join(", ", signatureParams);

        if (!string.IsNullOrWhiteSpace(operation.Summary))
        {
            b.Line("/**");
            b.Line($" * {operation.Summary}");
            b.Line(" */");
        }

        b.Line($"async {operation.Name}({paramsList}): Promise<{operation.ReturnType}> {{");
        b.Indent();
        b.Line($"return request({BuildUrl(operation.Path, operation.PathParams)}, '{operation.HttpMethod}'{BuildInitArgument(operation.QueryParams, operation.RequestBodyType != null)});");
        b.Dedent();
        b.Line("},");
    }

    private List<OperationInfo> ExtractOperations()
    {
        var operations = new List<OperationInfo>();
        var paths = _document.Paths;

        if (paths == null || paths.Count == 0)
        {
            return operations;
        }

        foreach (var pathEntry in paths)
        {
            string path = pathEntry.Key;

            // Skip auth endpoints meant for external clients (e.g. better-auth)
            if (IsAuthPath(path))
            {
                continue;
            }

            var opMap = pathEntry.Value.Operations;
            if (opMap == null)
            {
                continue;
            }

            foreach (var opEntry in opMap)
            {
                if (opEntry.Value == null)
                {
                    continue;
                }

                OperationInfo info = ExtractOperationInfo(path, opEntry.Key, opEntry.Value);
                operations.Add(info);
            }
        }

        return operations;
    }

    private OperationInfo ExtractOperationInfo(string path, HttpMethod method, OpenApiOperation operation)
    {
        string operationName = GetOperationName(path, method, operation);
        string httpMethod = method.Method.ToLowerInvariant();

        var pathParams = ExtractParameters(operation, ParameterLocation.Path);
        var queryParams = ExtractParameters(operation, ParameterLocation.Query);

        IOpenApiSchema? requestBodySchema = GetRequestBodySchema(operation);
        IOpenApiSchema? responseSchema = GetResponseSchema(operation);

        return new OperationInfo
        {
            Name = operationName,
            Path = path,
            HttpMethod = httpMethod,
            Summary = operation.Summary ?? string.Empty,
            PathParams = pathParams,
            QueryParams = queryParams,
            RequestBodySchema = requestBodySchema,
            ResponseSchema = responseSchema,
            ReturnType = "void",
        };
    }

    private static List<ParameterInfo> ExtractParameters(OpenApiOperation operation, ParameterLocation location)
    {
        var result = new List<ParameterInfo>();

        if (operation.Parameters == null)
        {
            return result;
        }

        foreach (var param in operation.Parameters)
        {
            if (param.In != location)
            {
                continue;
            }

            string paramName = SanitizeIdentifier(param.Name);
            string paramType = param.Schema != null
                ? OpenApiTypeMapper.MapSchema(param.Schema)
                : "string";

            result.Add(new ParameterInfo
            {
                OriginalName = param.Name,
                Name = paramName,
                Type = paramType,
                Required = param.Required,
            });
        }

        return result;
    }

    private string GetDefaultBaseUrl()
    {
        string? serverUrl = _document.Servers?.FirstOrDefault()?.Url;
        if (!string.IsNullOrWhiteSpace(serverUrl))
        {
            return serverUrl;
        }

        return "http://localhost";
    }

    private static string GetOperationName(string path, HttpMethod method, OpenApiOperation operation)
    {
        if (!string.IsNullOrWhiteSpace(operation.OperationId))
        {
            return SanitizeIdentifier(ToPascalCase(operation.OperationId));
        }

        string methodPrefix = method.Method.ToLowerInvariant();
        string pathSuffix = PathToPascalCase(path);

        if (string.IsNullOrEmpty(pathSuffix))
        {
            pathSuffix = "Root";
        }

        return SanitizeIdentifier(methodPrefix + pathSuffix);
    }

    private static string PathToPascalCase(string path)
    {
        string[] parts = path.Split(new[] { '/', '-', '_', '{', '}' }, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            return string.Empty;
        }

        // Strip redundant leading "api" segment
        int startIndex = 0;
        if (parts.Length > 1 && string.Equals(parts[0], "api", StringComparison.OrdinalIgnoreCase))
        {
            startIndex = 1;
        }

        var sb = new StringBuilder();

        for (int i = startIndex; i < parts.Length; i++)
        {
            string part = parts[i];
            if (part.Length > 0)
            {
                sb.Append(char.ToUpperInvariant(part[0]));
                if (part.Length > 1)
                {
                    sb.Append(part.Substring(1));
                }
            }
        }

        return sb.ToString();
    }

    private static string ToPascalCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unknown";
        }

        string[] parts = Regex.Split(value, @"[^a-zA-Z0-9]+");

        var sb = new StringBuilder();
        foreach (string part in parts)
        {
            if (string.IsNullOrEmpty(part))
            {
                continue;
            }

            sb.Append(char.ToUpperInvariant(part[0]));
            if (part.Length > 1)
            {
                string rest = part.Substring(1);
                if (rest.All(char.IsUpper))
                {
                    sb.Append(rest.ToLowerInvariant());
                }
                else
                {
                    sb.Append(rest);
                }
            }
        }

        return sb.ToString();
    }

    private static string SanitizeIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "_";
        }

        string sanitized = Regex.Replace(value, "[^a-zA-Z0-9_$]", "_");

        if (char.IsDigit(sanitized[0]))
        {
            sanitized = "_" + sanitized;
        }

        if (IsJavaScriptReservedWord(sanitized))
        {
            sanitized = "_" + sanitized;
        }

        if (sanitized == "_")
        {
            sanitized = "param";
        }

        return sanitized;
    }

    private static bool IsJavaScriptReservedWord(string word)
    {
        string[] reserved = new[]
        {
            "break", "case", "catch", "class", "const", "continue", "debugger", "default",
            "delete", "do", "else", "export", "extends", "finally", "for", "function",
            "if", "import", "in", "instanceof", "new", "return", "super", "switch",
            "this", "throw", "try", "typeof", "var", "void", "while", "with", "yield",
            "let", "static", "enum", "await", "implements", "interface", "package",
            "private", "protected", "public", "abstract", "boolean", "byte", "char",
            "double", "final", "float", "goto", "int", "long", "native", "short",
            "synchronized", "throws", "transient", "volatile", "null", "true", "false",
        };

        return reserved.Contains(word);
    }

    private static IOpenApiSchema? GetRequestBodySchema(OpenApiOperation operation)
    {
        if (operation.RequestBody?.Content == null)
        {
            return null;
        }

        if (operation.RequestBody.Content.TryGetValue("application/json", out var mediaType))
        {
            return mediaType.Schema;
        }

        return operation.RequestBody.Content.Values.FirstOrDefault()?.Schema;
    }

    private static IOpenApiSchema? GetResponseSchema(OpenApiOperation operation)
    {
        if (operation.Responses == null)
        {
            return null;
        }

        if (operation.Responses.TryGetValue("200", out var response))
        {
            return GetContentSchema(response);
        }

        if (operation.Responses.TryGetValue("201", out var response201))
        {
            return GetContentSchema(response201);
        }

        foreach (var entry in operation.Responses)
        {
            if (entry.Key.StartsWith("2"))
            {
                return GetContentSchema(entry.Value);
            }
        }

        return null;
    }

    private static IOpenApiSchema? GetContentSchema(IOpenApiResponse response)
    {
        if (response.Content == null)
        {
            return null;
        }

        if (response.Content.TryGetValue("application/json", out var mediaType))
        {
            return mediaType.Schema;
        }

        return response.Content.Values.FirstOrDefault()?.Schema;
    }

    private static bool IsAuthPath(string path)
    {
        return path.StartsWith("/api/auth/", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildUrl(string path, List<ParameterInfo> pathParams)
    {
        if (pathParams.Count == 0)
        {
            return $"'{path}'";
        }

        string url = path;

        foreach (var param in pathParams)
        {
            url = url.Replace($"{{{param.OriginalName}}}", $"${{encodeURIComponent({param.Name})}}");
        }

        return $"`{url}`";
    }

    private static string BuildInitArgument(List<ParameterInfo> queryParams, bool hasBody)
    {
        var parts = new List<string>();

        if (queryParams.Count > 0)
        {
            var names = queryParams.Select(p => $"{p.OriginalName}: {p.Name}");
            parts.Add("params: { " + string.Join(", ", names) + " }");
        }

        if (hasBody)
        {
            parts.Add("body");
        }

        if (parts.Count == 0)
        {
            return string.Empty;
        }

        return ", { " + string.Join(", ", parts) + " }";
    }

    private sealed class OperationInfo
    {
        public required string Name { get; init; }
        public required string Path { get; init; }
        public required string HttpMethod { get; init; }
        public required string Summary { get; init; }
        public List<ParameterInfo> PathParams { get; init; } = new List<ParameterInfo>();
        public List<ParameterInfo> QueryParams { get; init; } = new List<ParameterInfo>();
        public IOpenApiSchema? RequestBodySchema { get; init; }
        public IOpenApiSchema? ResponseSchema { get; init; }
        public string? RequestBodyType { get; set; }
        public string ReturnType { get; set; } = "void";
    }

    private sealed class ParameterInfo
    {
        public required string OriginalName { get; init; }
        public required string Name { get; init; }
        public required string Type { get; init; }
        public required bool Required { get; init; }
    }
}
