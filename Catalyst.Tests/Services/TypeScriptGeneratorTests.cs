namespace Catalyst.Tests.Services;

public class TypeScriptGeneratorTests
{
    private static OpenApiDocument ParseDocument(string json)
    {
        ReadResult result = OpenApiModelFactory.Parse(json, "json", new OpenApiReaderSettings());
        if (result.Document is null)
        {
            throw new InvalidOperationException("Failed to parse fixture document.");
        }

        return result.Document;
    }

    [Fact]
    public void Generate_DoesNotThrow_ForComponentSchemasWithRef()
    {
        string json = File.ReadAllText("Fixtures/openapi-ref.json");

        OpenApiDocument document = ParseDocument(json);
        TypeScriptGenerator generator = new TypeScriptGenerator(document, null);

        string code = generator.Generate();

        Assert.False(string.IsNullOrWhiteSpace(code));
        Assert.Contains("interface Pet", code);
        Assert.Contains("pets(headers?: Record<string, string>, signal?: AbortSignal): Promise<ApiResult<Pet[]>>", code);
        Assert.Contains("interface Get", code);
    }

    [Fact]
    public void Generate_SkipsConfiguredPaths_AndReportsCount()
    {
        string json = File.ReadAllText("Fixtures/openapi-ref.json");

        OpenApiDocument document = ParseDocument(json);
        TypeScriptGenerator generator = new TypeScriptGenerator(document, null, ["/api/auth/"]);

        generator.Generate();

        Assert.Equal(1, generator.SkippedPathCount);
        Assert.DoesNotContain("login", generator.Generate());
    }

    [Fact]
    public void Generate_DoesNotSkipPaths_WhenNoSkipListProvided()
    {
        string json = File.ReadAllText("Fixtures/openapi-ref.json");

        OpenApiDocument document = ParseDocument(json);
        TypeScriptGenerator generator = new TypeScriptGenerator(document, null);

        generator.Generate();

        Assert.Equal(0, generator.SkippedPathCount);
        Assert.Contains("login", generator.Generate());
    }

    [Fact]
    public void Generate_MatchesGoldenFile_ForExampleSpec()
    {
        string json = File.ReadAllText("Fixtures/openapi-example.json");

        OpenApiDocument document = ParseDocument(json);
        TypeScriptGenerator generator = new TypeScriptGenerator(
            document,
            "Enterprise API",
            generatedAt: new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero),
            generatorVersion: "0.2.0");

        string code = generator.Generate();
        string expected = File.ReadAllText("Fixtures/generated-api-example.ts");

        Assert.Equal(Normalize(expected), Normalize(code));
    }

    [Fact]
    public void Generate_VoidResponse_UsesTextInsteadOfJson()
    {
        string json = """
        {
            "openapi": "3.0.3",
            "info": { "title": "VoidTest", "version": "1.0" },
            "paths": {
                "/health": {
                    "get": {
                        "responses": {
                            "200": { "description": "No content" }
                        }
                    }
                },
                "/items": {
                    "post": {
                        "requestBody": {
                            "required": true,
                            "content": {
                                "application/json": {
                                    "schema": { "type": "object", "properties": { "name": { "type": "string" } } }
                                }
                            }
                        },
                        "responses": {
                            "204": { "description": "Created" }
                        }
                    }
                }
            }
        }
        """;

        OpenApiDocument document = ParseDocument(json);
        TypeScriptGenerator generator = new TypeScriptGenerator(document, "VoidTest");
        string code = generator.Generate();

        Assert.DoesNotContain("response.json()", code);
        Assert.Contains("response.text()", code);
        Assert.Contains("JSON.parse(text)", code);
        Assert.Contains("return { data: (text ? JSON.parse(text) : undefined) as T, error: null }", code);
    }

    [Fact]
    public void Generate_Signal_AppearsInRequestOptionsAndMethods()
    {
        string json = """
        {
            "openapi": "3.0.3",
            "info": { "title": "SignalTest", "version": "1.0" },
            "paths": {
                "/items": {
                    "get": {
                        "responses": {
                            "200": { "description": "OK", "content": { "application/json": { "schema": { "type": "string" } } } }
                        }
                    }
                }
            }
        }
        """;

        OpenApiDocument document = ParseDocument(json);
        TypeScriptGenerator generator = new TypeScriptGenerator(document, "SignalTest");
        string code = generator.Generate();

        Assert.Contains("signal?: AbortSignal", code);
        Assert.Contains("items(headers?: Record<string, string>, signal?: AbortSignal): Promise<ApiResult<string>>", code);
        Assert.Contains("async items(headers?: Record<string, string>, signal?: AbortSignal): Promise<ApiResult<string>>", code);
    }

    [Fact]
    public void Generate_TimeoutMs_CombinesWithCallerSignal()
    {
        string json = """
        {
            "openapi": "3.0.3",
            "info": { "title": "TimeoutTest", "version": "1.0" },
            "paths": {
                "/items": {
                    "get": {
                        "responses": {
                            "200": { "description": "OK", "content": { "application/json": { "schema": { "type": "string" } } } }
                        }
                    }
                }
            }
        }
        """;

        OpenApiDocument document = ParseDocument(json);
        TypeScriptGenerator generator = new TypeScriptGenerator(document, "TimeoutTest");
        string code = generator.Generate();

        Assert.Contains("timeoutMs?: number", code);
        Assert.Contains("const timeoutMs = options.timeoutMs;", code);
        Assert.Contains("const timeoutSignal = timeoutMs !== undefined ? AbortSignal.timeout(timeoutMs) : undefined;", code);
        Assert.Contains("let signal: AbortSignal | undefined = init?.signal;", code);
        Assert.Contains("signal = signal ? AbortSignal.any([signal, timeoutSignal]) : timeoutSignal;", code);
    }

    [Fact]
    public void Generate_Header_IncludesVersionAndGenerationTime()
    {
        string json = """
        {
            "openapi": "3.0.3",
            "info": { "title": "HeaderTest", "version": "1.0" },
            "paths": {
                "/items": {
                    "get": {
                        "responses": { "200": { "description": "OK" } }
                    }
                }
            }
        }
        """;

        OpenApiDocument document = ParseDocument(json);
        TypeScriptGenerator generator = new TypeScriptGenerator(
            document,
            "HeaderTest",
            generatedAt: new DateTimeOffset(2026, 10, 7, 9, 30, 0, TimeSpan.Zero),
            generatorVersion: "1.2.3");

        string code = generator.Generate();

        Assert.Contains("//  Generated by Catalyst 1.2.3 at 2026-10-07T09:30:00Z", code);
    }

    [Fact]
    public void Generate_Header_FallsBackToAssemblyVersion()
    {
        string json = """
        {
            "openapi": "3.0.3",
            "info": { "title": "VersionFallbackTest", "version": "1.0" },
            "paths": {
                "/items": {
                    "get": {
                        "responses": { "200": { "description": "OK" } }
                    }
                }
            }
        }
        """;

        OpenApiDocument document = ParseDocument(json);
        TypeScriptGenerator generator = new TypeScriptGenerator(document, "VersionFallbackTest");

        string code = generator.Generate();

        Assert.Matches(@"//  Generated by Catalyst \d+\.\d+\.\d+ at \d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z", code);
    }

    [Fact]
    public void Generate_BooleanQueryParameters_AcceptedByParamsType()
    {
        string json = """
        {
            "openapi": "3.0.3",
            "info": { "title": "BooleanQueryTest", "version": "1.0" },
            "paths": {
                "/items": {
                    "get": {
                        "parameters": [
                            { "name": "sms_enabled", "in": "query", "schema": { "type": "boolean" } }
                        ],
                        "responses": {
                            "200": { "description": "OK", "content": { "application/json": { "schema": { "type": "string" } } } }
                        }
                    }
                }
            }
        }
        """;

        OpenApiDocument document = ParseDocument(json);
        TypeScriptGenerator generator = new TypeScriptGenerator(document, "BooleanQueryTest");
        string code = generator.Generate();

        Assert.Contains("params?: Record<string, string | string[] | number | boolean | undefined>", code);
        Assert.Contains("smsEnabled?: boolean", code);
        Assert.Contains("params: { sms_enabled: smsEnabled }", code);
    }

    [Fact]
    public void Generate_DefaultResponseOnly_UsesDefaultSchemaAsReturnType()
    {
        string json = """
        {
            "openapi": "3.0.3",
            "info": { "title": "DefaultResponseTest", "version": "1.0" },
            "paths": {
                "/items": {
                    "get": {
                        "responses": {
                            "default": {
                                "description": "Success",
                                "content": { "application/json": { "schema": { "$ref": "#/components/schemas/ItemList" } } }
                            }
                        }
                    }
                }
            },
            "components": {
                "schemas": {
                    "ItemList": {
                        "type": "object",
                        "required": [ "items" ],
                        "properties": { "items": { "type": "array", "items": { "type": "string" } } }
                    }
                }
            }
        }
        """;

        OpenApiDocument document = ParseDocument(json);
        TypeScriptGenerator generator = new TypeScriptGenerator(document, "DefaultResponseTest");
        string code = generator.Generate();

        Assert.Contains("interface ItemList", code);
        Assert.Contains("items(headers?: Record<string, string>, signal?: AbortSignal): Promise<ApiResult<ItemList>>", code);
    }

    [Fact]
    public void Generate_ExplicitSuccessResponse_TakesPrecedenceOverDefault()
    {
        string json = """
        {
            "openapi": "3.0.3",
            "info": { "title": "PrecedenceTest", "version": "1.0" },
            "paths": {
                "/items": {
                    "get": {
                        "responses": {
                            "200": {
                                "description": "OK",
                                "content": { "application/json": { "schema": { "$ref": "#/components/schemas/SuccessBody" } } }
                            },
                            "default": {
                                "description": "Error",
                                "content": { "application/json": { "schema": { "$ref": "#/components/schemas/FailureBody" } } }
                            }
                        }
                    }
                }
            },
            "components": {
                "schemas": {
                    "SuccessBody": { "type": "object", "properties": { "ok": { "type": "boolean" } } },
                    "FailureBody": { "type": "object", "properties": { "detail": { "type": "string" } } }
                }
            }
        }
        """;

        OpenApiDocument document = ParseDocument(json);
        TypeScriptGenerator generator = new TypeScriptGenerator(document, "PrecedenceTest");
        string code = generator.Generate();

        Assert.Contains("Promise<ApiResult<SuccessBody>>", code);
        Assert.DoesNotContain("Promise<ApiResult<FailureBody>>", code);
    }

    [Fact]
    public void Generate_DefaultResponseWithoutSchema_UsesVoid()
    {
        string json = """
        {
            "openapi": "3.0.3",
            "info": { "title": "DefaultNoContentTest", "version": "1.0" },
            "paths": {
                "/items": {
                    "delete": {
                        "responses": {
                            "default": { "description": "No content" }
                        }
                    }
                }
            }
        }
        """;

        OpenApiDocument document = ParseDocument(json);
        TypeScriptGenerator generator = new TypeScriptGenerator(document, "DefaultNoContentTest");
        string code = generator.Generate();

        Assert.Contains("items(headers?: Record<string, string>, signal?: AbortSignal): Promise<ApiResult<void>>", code);
    }

    [Fact]
    public void Generate_ContentType_SetAutomaticallyForJsonBodies()
    {
        string json = """
        {
            "openapi": "3.0.3",
            "info": { "title": "ContentTypeTest", "version": "1.0" },
            "paths": {
                "/items": {
                    "post": {
                        "requestBody": {
                            "required": true,
                            "content": {
                                "application/json": {
                                    "schema": { "type": "object", "properties": { "name": { "type": "string" } } }
                                }
                            }
                        },
                        "responses": {
                            "200": { "description": "OK" }
                        }
                    }
                }
            }
        }
        """;

        OpenApiDocument document = ParseDocument(json);
        TypeScriptGenerator generator = new TypeScriptGenerator(document, "ContentTypeTest");
        string code = generator.Generate();

        Assert.Contains("requestHeaders[\"Content-Type\"] = \"application/json\"", code);
        Assert.Contains("!(\"Content-Type\" in requestHeaders)", code);
    }

    [Fact]
    public void Generate_CollisionSuffix_DeduplicatesMethodNames()
    {
        // /users/{user_id} and /users/{userId} both PascalCase to "UsersUserId" → collision
        string json = """
        {
            "openapi": "3.0.3",
            "info": { "title": "CollisionTest", "version": "1.0" },
            "paths": {
                "/users/{user_id}": {
                    "get": {
                        "parameters": [{ "name": "user_id", "in": "path", "required": true, "schema": { "type": "string" } }],
                        "responses": { "200": { "description": "OK" } }
                    }
                },
                "/users/{userId}": {
                    "get": {
                        "parameters": [{ "name": "userId", "in": "path", "required": true, "schema": { "type": "string" } }],
                        "responses": { "200": { "description": "OK" } }
                    }
                }
            }
        }
        """;

        OpenApiDocument document = ParseDocument(json);
        TypeScriptGenerator generator = new TypeScriptGenerator(document, "CollisionTest");
        string code = generator.Generate();

        // Should have distinct method names with suffix
        Assert.NotEmpty(generator.DuplicateWarnings);
        Assert.Contains("Duplicate method name", generator.DuplicateWarnings[0]);
    }

    [Fact]
    public void Generate_DuplicateWarnings_ReportsCollisions()
    {
        // /users/{user_id} and /users/{userId} both → "getUsersUserId"
        string json = """
        {
            "openapi": "3.0.3",
            "info": { "title": "WarnTest", "version": "1.0" },
            "paths": {
                "/users/{user_id}": {
                    "get": {
                        "parameters": [{ "name": "user_id", "in": "path", "required": true, "schema": { "type": "string" } }],
                        "responses": { "200": { "description": "OK" } }
                    }
                },
                "/users/{userId}": {
                    "get": {
                        "parameters": [{ "name": "userId", "in": "path", "required": true, "schema": { "type": "string" } }],
                        "responses": { "200": { "description": "OK" } }
                    }
                }
            }
        }
        """;

        OpenApiDocument document = ParseDocument(json);
        TypeScriptGenerator generator = new TypeScriptGenerator(document, "WarnTest");
        generator.Generate();

        Assert.Single(generator.DuplicateWarnings);
        Assert.Contains("Duplicate method name", generator.DuplicateWarnings[0]);
    }

    [Fact]
    public void Generate_BaseUrlTrailingSlash_HandledCorrectly()
    {
        string code = new TypeScriptGenerator(
            ParseDocument("""{"openapi":"3.0.3","info":{"title":"t","version":"1"},"paths":{"/x":{"get":{"responses":{"200":{"description":"OK"}}}}}}"""),
            "BaseUrlTest").Generate();

        Assert.Contains("baseUrl.replace(/\\/$/, \"\")", code);
        Assert.Contains("new URL(path, base || undefined)", code);
    }

    [Fact]
    public void Generate_EmitsEnums_AndNullableEnumReferences()
    {
        string json = """
        {
            "openapi": "3.1.1",
            "info": { "title": "EnumTest", "version": "1.0" },
            "paths": {
                "/organisations": {
                    "post": {
                        "requestBody": {
                            "required": true,
                            "content": {
                                "application/json": {
                                    "schema": { "$ref": "#/components/schemas/AddOrganisationRequest" }
                                }
                            }
                        },
                        "responses": { "200": { "description": "OK" } }
                    }
                }
            },
            "components": {
                "schemas": {
                    "AddOrganisationRequest": {
                        "type": "object",
                        "required": [ "displayName" ],
                        "properties": {
                            "displayName": { "type": "string" },
                            "communicationProvider": { "$ref": "#/components/schemas/CommunicationProviderType" },
                            "status": {
                                "oneOf": [
                                    { "type": "null" },
                                    { "$ref": "#/components/schemas/ProviderOrganisationStatus" }
                                ]
                            }
                        }
                    },
                    "CommunicationProviderType": { "enum": [ "Twilio" ] },
                    "ProviderOrganisationStatus": { "enum": [ "PendingCreation", "Active", null ] }
                }
            }
        }
        """;

        OpenApiDocument document = ParseDocument(json);
        TypeScriptGenerator generator = new TypeScriptGenerator(document, "EnumTest");
        string code = generator.Generate();

        Assert.Contains("export enum CommunicationProviderType", code);
        Assert.Contains("Twilio = \"Twilio\"", code);
        Assert.Contains("export enum ProviderOrganisationStatus", code);
        Assert.Contains("PendingCreation = \"PendingCreation\"", code);
        Assert.Contains("Active = \"Active\"", code);
        Assert.Contains("communicationProvider?: CommunicationProviderType", code);
        Assert.Contains("status?: ProviderOrganisationStatus | null", code);
        Assert.DoesNotContain("interface CommunicationProviderType", code);
        Assert.DoesNotContain("interface ProviderOrganisationStatus", code);
    }

    private static string Normalize(string value) =>
        value.Replace("\r\n", "\n").Trim();
}

