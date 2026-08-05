using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.OpenApi;
using UPhoricLibrary.CodeGeneration.Common;
using UPhoricLibrary.CodeGeneration.TypeScript;
using UPhoricLibrary.CodeGeneration.TypeScript.Builders;
using UPhoricLibrary.Extensions;

namespace Catalyst.Common.Services;

internal sealed class TypeScriptGenerator
{
    private readonly OpenApiDocument _document;
    private readonly string _className;
    private readonly HashSet<string> _generatedTypeNames;
    private readonly List<string> _skipPathPrefixes;

    private int _skippedPathCount;

    public TypeScriptGenerator(OpenApiDocument document, string? className, List<string>? skipPaths = null)
    {
        _document = document;
        _className = className ?? document.Info?.Title ?? "ApiClient";
        _generatedTypeNames = [];
        _skipPathPrefixes = skipPaths ?? [];
    }

    public List<string> DuplicateWarnings { get; } = [];

    public string Generate()
    {
        List<OperationInfo> operations = ExtractOperations();

        // FileBuilder defers all interface/function writes until EndFile(), so blank lines
        // inserted between builder creations would pile up at the top of the file. We disable
        // the library's section spacing and instead add blank lines between top-level exports
        // in a single post-processing pass over the final string.
        TypeScriptBuilder ts = new TypeScriptBuilder(new TypeScriptBuilderOptions { SectionSpacing = 0 });
        FileBuilder file = ts.File("api.ts");

        BuildHeader(file);
        BuildClientOptionsInterface(file);
        BuildRequestOptionsInterface(file);
        BuildSchemaInterfaces(file);
        BuildOperationTypeInterfaces(file, operations);
        BuildApiClientInterface(file, operations);
        BuildFactoryFunction(file, operations);

        file.EndFile();

        return InsertBlankLinesBetweenExports(ts.ToString());
    }

    private static string InsertBlankLinesBetweenExports(string code)
    {
        string lineEnding = code.Contains("\r\n") ? "\r\n" : "\n";
        string[] lines = code.Replace("\r\n", "\n").Split('\n');

        StringBuilder sb = new StringBuilder();
        bool firstExportSeen = false;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];

            if (line.StartsWith("export "))
            {
                if (firstExportSeen)
                {
                    sb.Append(lineEnding);
                }
                else
                {
                    firstExportSeen = true;
                }
            }

            sb.Append(line);

            if (i < lines.Length - 1)
            {
                sb.Append(lineEnding);
            }
        }

        return sb.ToString();
    }

    public int SkippedPathCount => _skippedPathCount;

    private void BuildHeader(FileBuilder file)
    {
        file.Comment($" Generated from {_className}");
        file.Comment(" Requires TypeScript target ES2015 or higher, or include ES2015 in lib");
        file.BlankLine();

        file.Interface("ProblemDetail<T = Record<string, unknown>>")
            .Export()
            .Property("type", "string")
                .Optional()
                .EndInterfaceProperty()
            .Property("title", "string")
                .Optional()
                .EndInterfaceProperty()
            .Property("status", "number")
                .Optional()
                .EndInterfaceProperty()
            .Property("detail", "string")
                .Optional()
                .EndInterfaceProperty()
            .Property("instance", "string")
                .Optional()
                .EndInterfaceProperty()
            .Property("extensions", "T")
                .Optional()
                .EndInterfaceProperty()
            .EndInterface();
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

    private static void BuildRequestOptionsInterface(FileBuilder file)
    {
        file.Interface("RequestOptions")
            .Export()
            .Property("params", "Record<string, string | string[] | number | undefined>")
                .Optional()
                .EndInterfaceProperty()
            .Property("body", "unknown")
                .Optional()
                .EndInterfaceProperty()
            .Property("headers", "Record<string, string>")
                .Optional()
                .EndInterfaceProperty()
            .Property("signal", "AbortSignal")
                .Optional()
                .EndInterfaceProperty()
            .EndInterface();
    }

    private void BuildSchemaInterfaces(FileBuilder file)
    {
        IDictionary<string, IOpenApiSchema>? schemas = _document.Components?.Schemas;
        if (schemas == null || schemas.Count == 0)
        {
            return;
        }

        foreach (KeyValuePair<string, IOpenApiSchema> schemaEntry in schemas)
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

        if (schema.Enum?.Count > 0)
        {
            BuildEnumFromSchema(file, name, schema);
            return;
        }

        InterfaceBuilder interfaceBuilder = file.Interface(name).Export();

        if (!string.IsNullOrWhiteSpace(schema.Description))
        {
            interfaceBuilder.WithJSDoc(js => js.Description(schema.Description.Trim()));
        }

        if (schema.Properties != null)
        {
            foreach (KeyValuePair<string, IOpenApiSchema> property in schema.Properties)
            {
                bool isRequired = schema.Required?.Contains(property.Key) ?? false;
                BuildInterfaceProperty(file, interfaceBuilder, name, property.Key, property.Value, isRequired);
            }
        }

        interfaceBuilder.EndInterface();
    }

    private void BuildEnumFromSchema(FileBuilder file, string name, IOpenApiSchema schema)
    {
        if (schema.Enum is not { Count: > 0 })
        {
            return;
        }

        EnumBuilder enumBuilder = file.Enum(SanitizeIdentifier(name)).Export();

        HashSet<string> usedMembers = [];

        foreach (JsonNode? member in schema.Enum)
        {
            if (member is not JsonValue jsonValue)
            {
                continue;
            }

            if (!jsonValue.TryGetValue(out string? value) || value is null)
            {
                continue;
            }

            string memberName = SanitizeIdentifier(value, asParameter: true);
            string uniqueName = memberName;
            int suffix = 1;
            while (!usedMembers.Add(uniqueName))
            {
                uniqueName = $"{memberName}_{suffix++}";
            }

            string escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
            enumBuilder.Member(uniqueName, $"\"{escaped}\"");
        }

        enumBuilder.EndEnum();
    }

    private void BuildInterfaceProperty(FileBuilder file, InterfaceBuilder interfaceBuilder, string parentName, string name, IOpenApiSchema schema, bool isRequired)
    {
        string propertyType = ResolveType(file, parentName + ToPascalCase(name), schema);

        PropertyBuilder propertyBuilder = interfaceBuilder.Property(name, propertyType);

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
                operation.RequestBodyType = ResolveType(file, operation.Name + "Request", operation.RequestBodySchema);
            }

            if (operation.ResponseSchema != null)
            {
                operation.ReturnType = ResolveType(file, operation.Name + "Response", operation.ResponseSchema);
            }
        }
    }

    private string ResolveType(FileBuilder file, string suggestedName, IOpenApiSchema schema)
    {
        if (schema is OpenApiSchemaReference schemaRef)
        {
            return schemaRef.Reference.Id ?? "any";
        }

        JsonSchemaType? type = schema.Type;
        JsonSchemaType effectiveType = (type ?? 0) & ~JsonSchemaType.Null;
        bool isNullable = type?.HasFlag(JsonSchemaType.Null) == true;

        if (schema.Items != null || effectiveType.HasFlag(JsonSchemaType.Array))
        {
            string itemType = schema.Items != null
                ? ResolveType(file, suggestedName + "Item", schema.Items)
                : "any";
            return isNullable ? itemType + "[] | null" : itemType + "[]";
        }

        if (schema.Properties?.Count > 0)
        {
            string pascalName = ToPascalCase(suggestedName);
            BuildInterfaceFromSchema(file, pascalName, schema);
            return isNullable ? pascalName + " | null" : pascalName;
        }

        if (schema.OneOf?.Count > 0)
        {
            return ResolveOneOf(file, suggestedName, schema);
        }

        return OpenApiTypeMapper.MapSchema(schema);
    }

    private string ResolveOneOf(FileBuilder file, string suggestedName, IOpenApiSchema schema)
    {
        if (schema.OneOf is not { Count: > 0 })
        {
            return "any";
        }

        List<string> branches = [];

        foreach (IOpenApiSchema branch in schema.OneOf)
        {
            bool isNullOnly = IsNullOnlySchema(branch);

            if (isNullOnly)
            {
                branches.Add("null");
            }
            else
            {
                branches.Add(ResolveType(file, suggestedName + "Union", branch));
            }
        }

        return string.Join(" | ", branches.Distinct().OrderBy(b => b == "null"));
    }

    private static bool IsNullOnlySchema(IOpenApiSchema schema)
    {
        JsonSchemaType? type = schema.Type;
        return type?.HasFlag(JsonSchemaType.Null) == true
            && (type.Value & ~JsonSchemaType.Null) == 0;
    }

    private void BuildApiClientInterface(FileBuilder file, List<OperationInfo> operations)
    {
        string interfaceName = SanitizeIdentifier(ToPascalCase(_className)) + "Client";
        InterfaceBuilder clientInterface = file.Interface(interfaceName).Export();

        IEnumerable<IGrouping<string, OperationInfo>> groups = operations.GroupBy(o => o.HttpMethod);

        foreach (IGrouping<string, OperationInfo> group in groups)
        {
            // Property on the main client: get: Get;
            string verbInterfaceName = SanitizeIdentifier(ToPascalCase(group.Key));
            clientInterface.Property(group.Key, verbInterfaceName).EndInterfaceProperty();

            // Named interface for this HTTP verb: export interface Get { ... }
            InterfaceBuilder verbInterface = file.Interface(verbInterfaceName).Export();

            foreach (OperationInfo operation in group)
            {
                string methodName = StripHttpVerbPrefix(operation.Name, operation.HttpMethod);
                MethodBuilder method = verbInterface.Method(methodName);

                foreach (ParameterInfo param in operation.PathParams)
                {
                    method.Parameter(param.Name, param.Type).EndMethodParameter();
                }

                foreach (ParameterInfo param in operation.QueryParams)
                {
                    ParameterBuilder parameter = method.Parameter(param.Name, param.Type);
                    if (!param.Required)
                    {
                        parameter.Optional();
                    }

                    parameter.EndMethodParameter();
                }

                if (operation.RequestBodyType != null)
                {
                    method.Parameter("body", operation.RequestBodyType).EndMethodParameter();
                }

                method.Parameter("headers", "Record<string, string>").Optional().EndMethodParameter();

                method.Returns("Promise<{ data: " + operation.ReturnType + "; error: null } | { data: null; error: ProblemDetail }>").EndMethod();
            }

            verbInterface.EndInterface();
        }

        clientInterface.EndInterface();
    }

    private void BuildFactoryFunction(FileBuilder file, List<OperationInfo> operations)
    {
        string interfaceName = SanitizeIdentifier(ToPascalCase(_className)) + "Client";
        string defaultBaseUrl = GetDefaultBaseUrl();
        bool hasDefaultUrl = !string.IsNullOrWhiteSpace(defaultBaseUrl);

        file.Function("createClient")
            .Export()
            .Parameter("options", "ClientOptions")
                .DefaultValue("{}")
                .EndFunctionParameter()
            .Returns(interfaceName)
            .Body(b =>
            {
                if (hasDefaultUrl)
                {
                    b.Line($"const baseUrl = options.baseUrl || '{defaultBaseUrl}';");
                }
                else
                {
                    b.Line("if (!options.baseUrl) {");
                    b.Indent();
                    b.Line("throw new Error('baseUrl is required in ClientOptions');");
                    b.Dedent();
                    b.Line("}");
                    b.Line("const baseUrl = options.baseUrl;");
                }
                b.Line("const credentials = options.credentials;");
                b.Line("const headers = options.headers || {};");
                b.BlankLine();

                b.Line("async function request<T>(path: string, method: string, init?: RequestOptions): Promise<{ data: T; error: null } | { data: null; error: ProblemDetail }> {");
                b.Indent();
                b.Line("const base = baseUrl.replace(/\\/$/, \"\");");
                b.Line("const url = new URL(path, base || undefined);");
                b.Line("if (init?.params) {");
                b.Indent();
                b.Line("const entries = Object.entries(init.params).filter(([, v]) => v !== undefined);");
                b.Line("for (const [key, value] of entries) {");
                b.Indent();
                b.Line("if (Array.isArray(value)) {");
                b.Indent();
                b.Line("for (const v of value) { url.searchParams.append(key, v); }");
                b.Dedent();
                b.Line("} else {");
                b.Indent();
                b.Line("url.searchParams.append(key, String(value));");
                b.Dedent();
                b.Line("}");
                b.Dedent();
                b.Line("}");
                b.Dedent();
                b.Line("}");
                b.BlankLine();
                b.Line("const requestHeaders: Record<string, string> = { ...headers, ...(init?.headers ?? {}) };");
                b.Line("let body: BodyInit | undefined;");
                b.Line("if (init?.body !== undefined) {");
                b.Indent();
                b.Line("if (typeof init.body === \"object\" && init.body !== null && !(init.body instanceof FormData) && !(init.body instanceof Blob) && !(init.body instanceof ArrayBuffer)) {");
                b.Indent();
                b.Line("if (!(\"Content-Type\" in requestHeaders)) {");
                b.Indent();
                b.Line("requestHeaders[\"Content-Type\"] = \"application/json\";");
                b.Dedent();
                b.Line("}");
                b.Line("body = JSON.stringify(init.body);");
                b.Dedent();
                b.Line("} else {");
                b.Indent();
                b.Line("body = init.body as BodyInit;");
                b.Dedent();
                b.Line("}");
                b.Dedent();
                b.Line("}");
                b.BlankLine();
                b.Line("const response = await fetch(url.href, {");
                b.Indent();
                b.Line("method,");
                b.Line("credentials,");
                b.Line("headers: requestHeaders,");
                b.Line("body,");
                b.Line("signal: init?.signal,");
                b.Dedent();
                b.Line("});");
                b.BlankLine();
                b.Line("if (!response.ok) {");
                b.Indent();
                b.Line("const errorText = await response.text().catch(() => \"\");");
                b.Line("let problemDetail: ProblemDetail = { status: response.status, title: response.statusText };");
                b.Line("try {");
                b.Indent();
                b.Line("const parsed = JSON.parse(errorText);");
                b.Line("if (parsed && typeof parsed === \"object\") {");
                b.Indent();
                b.Line("const { type, title, status, detail, instance, ...rest } = parsed;");
                b.Line("problemDetail = {");
                b.Indent();
                b.Line("type,");
                b.Line("title: title || response.statusText,");
                b.Line("status: status || response.status,");
                b.Line("detail: detail || errorText,");
                b.Line("instance,");
                b.Line("extensions: Object.keys(rest).length > 0 ? rest : undefined,");
                b.Dedent();
                b.Line("};");
                b.Dedent();
                b.Line("}");
                b.Dedent();
                b.Line("} catch {");
                b.Indent();
                b.Line("problemDetail = { status: response.status, title: response.statusText, detail: errorText || `HTTP ${response.status}` };");
                b.Dedent();
                b.Line("}");
                b.Line("return { data: null, error: problemDetail };");
                b.Dedent();
                b.Line("}");
                b.BlankLine();
                b.Line("const text = await response.text();");
                b.Line("return { data: (text ? JSON.parse(text) : undefined) as T, error: null };");
                b.Dedent();
                b.Line("}");
                b.BlankLine();

                b.Line("return {");
                b.Indent();

                IEnumerable<IGrouping<string, OperationInfo>> methodGroups = operations.GroupBy(o => o.HttpMethod);
                int groupIndex = 0;
                int totalGroups = methodGroups.Count();

                foreach (IGrouping<string, OperationInfo> group in methodGroups)
                {
                    b.Line($"{group.Key}: {{");
                    b.Indent();

                    OperationInfo[] groupOps = group.ToArray();
                    for (int i = 0; i < groupOps.Length; i++)
                    {
                        OperationInfo operation = groupOps[i];
                        WriteFactoryMethodBody(b, operation);

                        if (i < groupOps.Length - 1)
                        {
                            b.BlankLine();
                        }
                    }

                    b.Dedent();
                    b.Line("},");

                    if (groupIndex < totalGroups - 1)
                    {
                        b.BlankLine();
                    }

                    groupIndex++;
                }

                b.Dedent();
                b.Line("};");
            })
            .EndFunction();
    }

    private static void WriteFactoryMethodBody(BodyBuilder b, OperationInfo operation)
    {
        List<string> signatureParams = [];

        foreach (ParameterInfo param in operation.PathParams)
        {
            signatureParams.Add($"{param.Name}: {param.Type}");
        }

        foreach (ParameterInfo param in operation.QueryParams)
        {
            string optional = param.Required ? string.Empty : "?";
            signatureParams.Add($"{param.Name}{optional}: {param.Type}");
        }

        if (operation.RequestBodyType != null)
        {
            signatureParams.Add($"body: {operation.RequestBodyType}");
        }

        signatureParams.Add("headers?: Record<string, string>");
        signatureParams.Add("signal?: AbortSignal");

        string paramsList = string.Join(", ", signatureParams);
        string methodName = StripHttpVerbPrefix(operation.Name, operation.HttpMethod);

        if (!string.IsNullOrWhiteSpace(operation.Summary))
        {
            b.Line(BuildJSDoc(operation.Summary));
        }

        b.Line($"async {methodName}({paramsList}): Promise<{{ data: {operation.ReturnType}; error: null }} | {{ data: null; error: ProblemDetail }}> {{");
        b.Indent();
        b.Line($"return request<{operation.ReturnType}>({BuildUrl(operation.Path, operation.PathParams)}, '{operation.HttpMethod}'{BuildInitArgument(operation.QueryParams, operation.RequestBodyType != null)});");
        b.Dedent();
        b.Line("},");
    }

    private List<OperationInfo> ExtractOperations()
    {
        List<OperationInfo> operations = [];
        Dictionary<string, Dictionary<string, (string Path, string Method)>> usedNamesByMethod = [];
        OpenApiPaths? paths = _document.Paths;

        if (paths == null || paths.Count == 0)
        {
            return operations;
        }

        foreach (KeyValuePair<string, IOpenApiPathItem> pathEntry in paths)
        {
            string path = pathEntry.Key;

            // Skip paths matching the configured skip prefixes
            if (IsSkippedPath(path))
            {
                _skippedPathCount++;
                continue;
            }

            Dictionary<HttpMethod, OpenApiOperation>? opMap = pathEntry.Value.Operations;
            if (opMap == null)
            {
                continue;
            }

            foreach (KeyValuePair<HttpMethod, OpenApiOperation> opEntry in opMap)
            {
                if (opEntry.Value == null)
                {
                    continue;
                }

                string httpMethod = opEntry.Key.Method.ToLowerInvariant();
                if (!usedNamesByMethod.ContainsKey(httpMethod))
                {
                    usedNamesByMethod[httpMethod] = [];
                }

                OperationInfo info = ExtractOperationInfo(path, opEntry.Key, opEntry.Value, usedNamesByMethod[httpMethod]);
                operations.Add(info);
            }
        }

        return operations;
    }

    private OperationInfo ExtractOperationInfo(string path, HttpMethod method, OpenApiOperation operation, Dictionary<string, (string Path, string Method)> usedNames)
    {
        string operationName = GetOperationName(path, method, operation, usedNames);
        string httpMethod = method.Method.ToLowerInvariant();

        List<ParameterInfo> pathParams = ExtractParameters(operation, ParameterLocation.Path);
        List<ParameterInfo> queryParams = ExtractParameters(operation, ParameterLocation.Query);

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
        List<ParameterInfo> result = [];

        if (operation.Parameters == null)
        {
            return result;
        }

        foreach (IOpenApiParameter param in operation.Parameters)
        {
            if (param.In != location)
            {
                continue;
            }

            string rawName = param.Name ?? "param";
            // Only normalize separator-delimited names (e.g. "user-id" -> "userId").
            // Already-camelCase names like "userId" must be left untouched, since the
            // library's ToCamelCase would otherwise lowercase the inner capital.
            string camelName = rawName.Any(ch => ch is '-' or '_' or ' ')
                ? rawName.ToCamelCase()
                : rawName;
            string paramName = SanitizeIdentifier(camelName, asParameter: true);
            string paramType = param.Schema != null
                ? OpenApiTypeMapper.MapSchema(param.Schema)
                : "string";

            result.Add(new ParameterInfo
            {
                OriginalName = param.Name ?? paramName,
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

        return string.Empty;
    }

    private string GetOperationName(string path, HttpMethod method, OpenApiOperation operation, Dictionary<string, (string Path, string Method)> usedNames)
    {
        string candidate;
        if (!string.IsNullOrWhiteSpace(operation.OperationId))
        {
            candidate = SanitizeIdentifier(ToPascalCase(operation.OperationId));
        }
        else
        {
            string methodPrefix = method.Method.ToLowerInvariant();
            string pathSuffix = PathToPascalCase(path);

            if (string.IsNullOrEmpty(pathSuffix))
            {
                pathSuffix = "Root";
            }

            candidate = SanitizeIdentifier(methodPrefix + pathSuffix);
        }

        string baseName = candidate;
        if (!usedNames.ContainsKey(candidate))
        {
            usedNames[candidate] = (path, method.Method);
            return candidate;
        }

        // Duplicate detected — derive a readable suffix from the raw path
        string suffix = BuildDuplicateSuffix(path, method.Method, usedNames[baseName].Path, usedNames[baseName].Method);
        candidate = SanitizeIdentifier(baseName + suffix);

        // Should be unique now, but handle edge case
        int counter = 2;
        while (usedNames.ContainsKey(candidate))
        {
            candidate = SanitizeIdentifier(baseName + suffix + counter);
            counter++;
        }

        usedNames[candidate] = (path, method.Method);

        string existingPath = usedNames[baseName].Path;
        string existingMethod = usedNames[baseName].Method;
        DuplicateWarnings.Add($"Duplicate method name \"{baseName}\" for {existingMethod} {existingPath} and {method.Method} {path}. Suffix added: \"{candidate}\".");

        return candidate;
    }

    private static string BuildDuplicateSuffix(string path1, string method1, string path2, string method2)
    {
        string[] segments1 = path1.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string[] segments2 = path2.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Find the last segment where paths differ
        int minLen = Math.Min(segments1.Length, segments2.Length);
        for (int i = minLen - 1; i >= 0; i--)
        {
            string s1 = segments1[i];
            string s2 = segments2[i];
            if (!string.Equals(s1, s2, StringComparison.OrdinalIgnoreCase))
            {
                return "_" + SanitizeIdentifier(ToPascalCase(s1));
            }
        }

        // Paths are structurally identical — use param names if present
        string lastParam1 = ExtractLastParamName(path1);
        string lastParam2 = ExtractLastParamName(path2);
        if (!string.IsNullOrEmpty(lastParam1) || !string.IsNullOrEmpty(lastParam2))
        {
            string p1 = string.IsNullOrEmpty(lastParam1) ? "Default" : ToPascalCase(lastParam1);
            string p2 = string.IsNullOrEmpty(lastParam2) ? "Default" : ToPascalCase(lastParam2);
            if (p1 != p2)
            {
                return "_" + SanitizeIdentifier(p1);
            }
        }

        // Fallback — should not reach here
        return "_V2";
    }

    private static string ExtractLastParamName(string path)
    {
        int lastOpen = path.LastIndexOf('{');
        int lastClose = path.LastIndexOf('}');
        if (lastOpen >= 0 && lastClose > lastOpen)
        {
            return path.Substring(lastOpen + 1, lastClose - lastOpen - 1).TrimStart('*');
        }

        return string.Empty;
    }

    private static string PathToPascalCase(string path)
    {
        string[] parts = path.Split(['/', '-', '_', '{', '}', '*'], StringSplitOptions.RemoveEmptyEntries);

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

        StringBuilder sb = new StringBuilder();

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

        // Insert separators at camelCase boundaries so the library's ToPascalCase
        // treats each word independently (it otherwise lowercases the trailing text).
        string separated = Regex.Replace(value, "(?<=[a-z0-9])(?=[A-Z])", " ");
        return separated.ToPascalCase();
    }

    private static string SanitizeIdentifier(string value)
    {
        return SanitizeIdentifier(value, asParameter: false);
    }

    private static string SanitizeIdentifier(string value, bool asParameter)
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

        // Reserved words can't be used as bare identifiers in TypeScript.
        //  - As property/method/type names they are valid when quoted ("class").
        //  - As parameter bindings they must stay valid identifiers, so prefix "_".
        if (IsJavaScriptReservedWord(sanitized))
        {
            return asParameter ? "_" + sanitized : "\"" + sanitized + "\"";
        }

        if (sanitized == "_")
        {
            sanitized = "param";
        }

        return sanitized;
    }

    private static string BuildJSDoc(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return string.Empty;
        }

        string trimmed = description.Trim().Replace("\r", string.Empty).Replace("\n", " ");
        return $"/** {trimmed} */";
    }

    private static string StripHttpVerbPrefix(string name, string httpMethod)
    {
        if (name.Length > httpMethod.Length && name.StartsWith(httpMethod, StringComparison.OrdinalIgnoreCase))
        {
            string stripped = name.Substring(httpMethod.Length);
            return char.ToLowerInvariant(stripped[0]) + stripped.Substring(1);
        }

        return name;
    }

    private static bool IsJavaScriptReservedWord(string word)
    {
        string[] reserved =
        [
            "break", "case", "catch", "class", "const", "continue", "debugger", "default",
            "delete", "do", "else", "export", "extends", "finally", "for", "function",
            "if", "import", "in", "instanceof", "new", "return", "super", "switch",
            "this", "throw", "try", "typeof", "var", "void", "while", "with", "yield",
            "let", "static", "enum", "await", "implements", "interface", "package",
            "private", "protected", "public", "abstract", "boolean", "byte", "char",
            "double", "final", "float", "goto", "int", "long", "native", "short",
            "synchronized", "throws", "transient", "volatile", "null", "true", "false"
        ];

        return reserved.Contains(word);
    }

    private static IOpenApiSchema? GetRequestBodySchema(OpenApiOperation operation)
    {
        if (operation.RequestBody?.Content == null)
        {
            return null;
        }

        if (operation.RequestBody.Content.TryGetValue("application/json", out IOpenApiMediaType? mediaType))
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

        if (operation.Responses.TryGetValue("200", out IOpenApiResponse? response))
        {
            return GetContentSchema(response);
        }

        if (operation.Responses.TryGetValue("201", out IOpenApiResponse? response201))
        {
            return GetContentSchema(response201);
        }

        foreach (KeyValuePair<string, IOpenApiResponse> entry in operation.Responses)
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

        if (response.Content.TryGetValue("application/json", out IOpenApiMediaType? mediaType))
        {
            return mediaType.Schema;
        }

        return response.Content.Values.FirstOrDefault()?.Schema;
    }

    private bool IsSkippedPath(string path)
    {
        if (_skipPathPrefixes.Count == 0)
        {
            return false;
        }

        return _skipPathPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildUrl(string path, List<ParameterInfo> pathParams)
    {
        if (pathParams.Count == 0)
        {
            return $"'{path}'";
        }

        string url = path;

        foreach (ParameterInfo param in pathParams)
        {
            url = url.Replace($"{{{param.OriginalName}}}", $"${{encodeURIComponent({param.Name})}}");
        }

        return $"`{url}`";
    }

    private static string BuildInitArgument(List<ParameterInfo> queryParams, bool hasBody)
    {
        List<string> parts = [];

        if (queryParams.Count > 0)
        {
            IEnumerable<string> names = queryParams.Select(p => p.OriginalName == p.Name ? p.Name : $"{p.OriginalName}: {p.Name}");
            parts.Add("params: { " + string.Join(", ", names) + " }");
        }

        if (hasBody)
        {
            parts.Add("body");
        }

        parts.Add("headers");
        parts.Add("signal");

        return ", { " + string.Join(", ", parts) + " }";
    }

    private sealed class OperationInfo
    {
        public required string Name { get; init; }
        public required string Path { get; init; }
        public required string HttpMethod { get; init; }
        public required string Summary { get; init; }
        public List<ParameterInfo> PathParams { get; init; } = [];
        public List<ParameterInfo> QueryParams { get; init; } = [];
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
