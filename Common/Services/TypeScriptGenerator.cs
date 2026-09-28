using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.OpenApi;
using Auxil.CodeGeneration.Common;
using Auxil.CodeGeneration.Common.Builders;
using Auxil.CodeGeneration.Common.Builders.Scopes;
using Auxil.CodeGeneration.Common.Languages;
using Auxil.CodeGeneration.Common.Model;
using Auxil.Extensions;

namespace Catalyst.Common.Services;

internal sealed class TypeScriptGenerator
{
    private readonly OpenApiDocument _document;
    private readonly string _className;
    private readonly HashSet<string> _generatedTypeNames;
    private readonly List<string> _skipPathPrefixes;

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

        CodeBuilder<TypeScript> ts = new(new CodeBuilderOptions { BraceStyle = BraceStyle.KAndR });
        FileScope<TypeScript> file = ts.File("api.ts");

        BuildHeader(file);
        BuildClientOptionsInterface(file);
        BuildRequestOptionsInterface(file);
        BuildSchemaInterfaces(file);
        BuildOperationTypeInterfaces(file, operations);
        BuildApiClientInterface(file, operations);
        BuildFactoryFunction(file, operations);

        file.EndFile();

        return ts.Render();
    }

    public int SkippedPathCount { get; private set; }

    private void BuildHeader(FileScope<TypeScript> file)
    {
        file.Comment(" Generated from " + _className + "\n Requires TypeScript target ES2015 or higher, or include ES2015 in lib");

        file.Interface("ProblemDetail")
            .Generic("T", null, "Record<string, unknown>")
            .Export()
            .Property("type", "string")
                .Nullable()
                .EndProperty()
            .Property("title", "string")
                .Nullable()
                .EndProperty()
            .Property("status", "number")
                .Nullable()
                .EndProperty()
            .Property("detail", "string")
                .Nullable()
                .EndProperty()
            .Property("instance", "string")
                .Nullable()
                .EndProperty()
            .Property("extensions", "T")
                .Nullable()
                .EndProperty()
            .EndInterface();

        file.TypeAlias("ApiResult<T>", "{ data: T; error: null } | { data: null; error: ProblemDetail }")
            .Export()
            .EndTypeAlias();
    }

    private static void BuildClientOptionsInterface(FileScope<TypeScript> file)
    {
        file.Interface("ClientOptions")
            .Export()
            .Property("baseUrl", "string")
                .Nullable()
                .EndProperty()
            .Property("credentials", "RequestCredentials")
                .Nullable()
                .EndProperty()
            .Property("headers", "Record<string, string>")
                .Nullable()
                .EndProperty()
            .EndInterface();
    }

    private static void BuildRequestOptionsInterface(FileScope<TypeScript> file)
    {
        file.Interface("RequestOptions")
            .Export()
            .Property("params", "Record<string, string | string[] | number | boolean | undefined>")
                .Nullable()
                .EndProperty()
            .Property("body", "unknown")
                .Nullable()
                .EndProperty()
            .Property("headers", "Record<string, string>")
                .Nullable()
                .EndProperty()
            .Property("signal", "AbortSignal")
                .Nullable()
                .EndProperty()
            .EndInterface();
    }

    private void BuildSchemaInterfaces(FileScope<TypeScript> file)
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

    private void BuildInterfaceFromSchema(FileScope<TypeScript> file, string name, IOpenApiSchema schema)
    {
        if (!_generatedTypeNames.Add(name))
        {
            return;
        }

        if (schema.Enum?.Count > 0)
        {
            BuildEnumFromSchema(file, name, schema);
            return;
        }

        InterfaceScope<TypeScript, FileScope<TypeScript>> interfaceBuilder = file.Interface(name).Export();

        if (!string.IsNullOrWhiteSpace(schema.Description))
        {
            interfaceBuilder.Doc(schema.Description.Trim());
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

    private static void BuildEnumFromSchema(FileScope<TypeScript> file, string name, IOpenApiSchema schema)
    {
        if (schema.Enum is not { Count: > 0 })
        {
            return;
        }

        EnumScope<TypeScript, FileScope<TypeScript>> enumBuilder = file.Enum(SanitizeIdentifier(name)).Export();

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

    private void BuildInterfaceProperty(FileScope<TypeScript> file, InterfaceScope<TypeScript, FileScope<TypeScript>> interfaceBuilder, string parentName, string name, IOpenApiSchema schema, bool isRequired)
    {
        string propertyType = ResolveType(file, parentName + ToPascalCase(name), schema);

        PropertyScope<TypeScript, InterfaceScope<TypeScript, FileScope<TypeScript>>> propertyBuilder = interfaceBuilder.Property(name, propertyType);

        if (!isRequired)
        {
            propertyBuilder.Nullable();
        }

        propertyBuilder.EndProperty();
    }

    private void BuildOperationTypeInterfaces(FileScope<TypeScript> file, List<OperationInfo> operations)
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

    private string ResolveType(FileScope<TypeScript> file, string suggestedName, IOpenApiSchema schema)
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

    private string ResolveOneOf(FileScope<TypeScript> file, string suggestedName, IOpenApiSchema schema)
    {
        if (schema.OneOf is not { Count: > 0 })
        {
            return "any";
        }

        List<string> branches = [];

        foreach (IOpenApiSchema branch in schema.OneOf)
        {
            bool isNullOnly = IsNullOnlySchema(branch);

            branches.Add(isNullOnly ? "null" : ResolveType(file, suggestedName + "Union", branch));
        }

        return string.Join(" | ", branches.Distinct().OrderBy(b => b == "null"));
    }

    private static bool IsNullOnlySchema(IOpenApiSchema schema)
    {
        JsonSchemaType? type = schema.Type;
        return type?.HasFlag(JsonSchemaType.Null) == true
            && (type.Value & ~JsonSchemaType.Null) == 0;
    }

    private void BuildApiClientInterface(FileScope<TypeScript> file, List<OperationInfo> operations)
    {
        string interfaceName = SanitizeIdentifier(ToPascalCase(_className)) + "Client";
        InterfaceScope<TypeScript, FileScope<TypeScript>> clientInterface = file.Interface(interfaceName).Export();

        IEnumerable<IGrouping<string, OperationInfo>> groups = operations.GroupBy(o => o.HttpMethod);

        foreach (IGrouping<string, OperationInfo> group in groups)
        {
            // Property on the main client: get: Get;
            string verbInterfaceName = SanitizeIdentifier(ToPascalCase(group.Key));
            clientInterface.Property(group.Key, verbInterfaceName).EndProperty();

            // Named interface for this HTTP verb: export interface Get { ... }
            InterfaceScope<TypeScript, FileScope<TypeScript>> verbInterface = file.Interface(verbInterfaceName).Export();

            foreach (OperationInfo operation in group)
            {
                string methodName = StripHttpVerbPrefix(operation.Name, operation.HttpMethod);
                MethodScope<TypeScript, InterfaceScope<TypeScript, FileScope<TypeScript>>> method = verbInterface.Method(methodName);

                foreach (ParameterInfo param in operation.PathParams)
                {
                    method.Parameter(param.Name, param.Type);
                }

                foreach (ParameterInfo param in operation.QueryParams)
                {
                    method.Parameter(param.Name, param.Type);
                    if (!param.Required)
                    {
                        method.Nullable();
                    }
                }

                if (operation.RequestBodyType != null)
                {
                    method.Parameter("body", operation.RequestBodyType);
                }

                method.Parameter("headers", "Record<string, string>").Nullable();

                method.Returns("Promise<ApiResult<" + operation.ReturnType + ">>").EndMethod();
            }

            verbInterface.EndInterface();
        }

        clientInterface.EndInterface();
    }

    private void BuildFactoryFunction(FileScope<TypeScript> file, List<OperationInfo> operations)
    {
        string interfaceName = SanitizeIdentifier(ToPascalCase(_className)) + "Client";
        string defaultBaseUrl = GetDefaultBaseUrl();
        bool hasDefaultUrl = !string.IsNullOrWhiteSpace(defaultBaseUrl);

        file.Function("createClient")
            .Export()
            .Parameter("options", "ClientOptions", "{}")
            .Returns(interfaceName)
            .Body(b =>
            {
                if (hasDefaultUrl)
                {
                    b.Const("baseUrl", (CodeType?)null, $"options.baseUrl || '{defaultBaseUrl}'");
                }
                else
                {
                    b.If("!options.baseUrl", i => i.Throw("new Error('baseUrl is required in ClientOptions')"));
                    b.Const("baseUrl", (CodeType?)null, "options.baseUrl");
                }
                b.Const("credentials", (CodeType?)null, "options.credentials");
                b.Const("headers", (CodeType?)null, "options.headers || {}");
                b.BlankLine();

                b.Function("request", f => f
                    .Async()
                    .Generic("T")
                    .Parameter("path", "string")
                    .Parameter("method", "string")
                    .Parameter("init", "RequestOptions").Nullable()
                    .Returns("Promise<ApiResult<T>>")
                    .Body(req =>
                    {
                        req.Const("base", (CodeType?)null, "baseUrl.replace(/\\/$/, \"\")");
                        req.Const("url", (CodeType?)null, "new URL(path, base || undefined)");
                        req.If("init?.params", p =>
                        {
                            p.Const("entries", (CodeType?)null, "Object.entries(init.params).filter(([, v]) => v !== undefined)");
                            p.ForEach("[key, value]", "entries", e =>
                            {
                                e.If("Array.isArray(value)", arr =>
                                    arr.ForEach("v", "value", v => v.Line("url.searchParams.append(key, v);")))
                                 .Else(other => other.Line("url.searchParams.append(key, String(value));"));
                            });
                        });
                        req.BlankLine();
                        req.Const("requestHeaders", "Record<string, string>", "{ ...headers, ...(init?.headers ?? {}) }");
                        req.Var("body", "BodyInit | undefined");
                        req.If("init?.body !== undefined", body =>
                        {
                            body.If("typeof init.body === \"object\" && init.body !== null && !(init.body instanceof FormData) && !(init.body instanceof Blob) && !(init.body instanceof ArrayBuffer)", obj =>
                            {
                                obj.If("!(\"Content-Type\" in requestHeaders)", ct =>
                                    ct.Line("requestHeaders[\"Content-Type\"] = \"application/json\";"));
                                obj.Line("body = JSON.stringify(init.body);");
                            }).Else(raw => raw.Line("body = init.body as BodyInit;"));
                        });
                        req.BlankLine();
                        req.Line("const response = await fetch(url.href, {");
                        req.Line("    method,");
                        req.Line("    credentials,");
                        req.Line("    headers: requestHeaders,");
                        req.Line("    body,");
                        req.Line("    signal: init?.signal,");
                        req.Line("});");
                        req.BlankLine();
                        req.If("!response.ok", err =>
                        {
                            err.Const("errorText", (CodeType?)null, "await response.text().catch(() => \"\")");
                            err.Var("problemDetail", "ProblemDetail", "{ status: response.status, title: response.statusText }");
                            err.Try(t =>
                            {
                                t.Const("parsed", (CodeType?)null, "JSON.parse(errorText)");
                                t.If("parsed && typeof parsed === \"object\"", parsed =>
                                {
                                    parsed.Line("const { type, title, status, detail, instance, ...rest } = parsed;");
                                    parsed.Line("problemDetail = {");
                                    parsed.Line("    type,");
                                    parsed.Line("    title: title || response.statusText,");
                                    parsed.Line("    status: status || response.status,");
                                    parsed.Line("    detail: detail || errorText,");
                                    parsed.Line("    instance,");
                                    parsed.Line("    extensions: Object.keys(rest).length > 0 ? rest : undefined,");
                                    parsed.Line("};");
                                });
                            }).Catch(null, c =>
                            {
                                c.Line("problemDetail = { status: response.status, title: response.statusText, detail: errorText || `HTTP ${response.status}` };");
                            });
                            err.Return("{ data: null, error: problemDetail }");
                        });
                        req.BlankLine();
                        req.Const("text", (CodeType?)null, "await response.text()");
                        req.Return("{ data: (text ? JSON.parse(text) : undefined) as T, error: null }");
                    }));
                b.BlankLine();

                b.ObjectLiteral("return", obj =>
                {
                    IEnumerable<IGrouping<string, OperationInfo>> methodGroups = operations.GroupBy(o => o.HttpMethod);

                    foreach (IGrouping<string, OperationInfo> group in methodGroups)
                    {
                        obj.Member(group.Key, g =>
                        {
                            foreach (OperationInfo operation in group.ToArray())
                            {
                                string methodName = StripHttpVerbPrefix(operation.Name, operation.HttpMethod);

                                g.Member(methodName, m =>
                                {
                                    if (!string.IsNullOrWhiteSpace(operation.Summary))
                                    {
                                        m.Doc(operation.Summary);
                                    }

                                    m.Async();

                                    foreach (ParameterInfo param in operation.PathParams)
                                    {
                                        m.Parameter(param.Name, param.Type);
                                    }

                                    foreach (ParameterInfo param in operation.QueryParams)
                                    {
                                        m.Parameter(param.Name, param.Type);
                                        if (!param.Required)
                                        {
                                            m.Nullable();
                                        }
                                    }

                                    if (operation.RequestBodyType != null)
                                    {
                                        m.Parameter("body", operation.RequestBodyType);
                                    }

                                    m.Parameter("headers", "Record<string, string>").Nullable();
                                    m.Parameter("signal", "AbortSignal").Nullable();
                                    m.Returns($"Promise<ApiResult<{operation.ReturnType}>>");
                                    m.Body(fn => fn.Return($"request<{operation.ReturnType}>({BuildUrl(operation.Path, operation.PathParams)}, '{operation.HttpMethod}'{BuildInitArgument(operation.QueryParams, operation.RequestBodyType != null)})"));
                                });
                            }
                        });
                    }
                });
            })
            .EndFunction();
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
                SkippedPathCount++;
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

        // Duplicate detected â€” derive a readable suffix from the raw path
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

        // Paths are structurally identical â€” use param names if present
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

        // Fallback â€” should not reach here
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

        // Some APIs (e.g. ASP.NET Core's ProducesDefaultResponseType) describe
        // the success payload as the `default` response instead of a 2xx status.
        // Fall back to it when no success response is declared.
        foreach (KeyValuePair<string, IOpenApiResponse> entry in operation.Responses)
        {
            if (entry.Key.Equals("default", StringComparison.OrdinalIgnoreCase))
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


