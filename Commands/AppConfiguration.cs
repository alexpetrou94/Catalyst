using Catalyst.Commands.Api;
using Catalyst.Commands.Gen;
using Catalyst.Commands.Mcp;
using Spectre.Console.Cli;

namespace Catalyst.Commands;

/// <summary>The command tree of the CLI, shared by Program and the command tests.</summary>
internal static class AppConfiguration
{
    public static void Configure(IConfigurator config, string[] args, string version)
    {
        // Unknown options and stray arguments are errors, and every usage error exits with the same code.
        UsageErrors.Configure(config, args);
        config.SetApplicationVersion(version);
        config.AddCommand<McpCommand>("mcp")
            .WithDescription("Runs a Model Context Protocol server over stdio, so AI agents (Claude Code, Claude Desktop, Cursor, VS Code) can call Catalyst's lookup, diff and generate tools.")
            .WithExample("mcp");
        config.AddBranch("api", api =>
        {
            api.SetDescription("Look up operations and types of an API, with the names and types of the generated client");
            api.AddCommand<ApiListCommand>("list")
                .WithDescription("Lists the operations of an API: method, path, the generated call and the summary. Filter with --filter, --tag or --method.")
                .WithExample("api", "list", "--filter", "pets")
                .WithExample("api", "list", "--client", "PetStore", "--method", "post", "--json");
            api.AddCommand<ApiShowCommand>("show")
                .WithDescription("Shows one operation: the exact generated call, its parameters, body and response, and the TypeScript declarations of the types it uses.")
                .WithExample("api", "show", "GET /pets/{id}")
                .WithExample("api", "show", "client.get.petById", "--no-types");
            api.AddCommand<ApiDiffCommand>("diff")
            .WithDescription("Reports breaking and non-breaking changes between an old spec (--base) and the current one, in terms of the generated client.")
            .WithExample("api", "diff", "--base", "./previous-openapi.json", "--source", "./openapi.json")
            .WithExample("api", "diff", "--client", "PetStore", "--base", "git:HEAD", "--fail-on-breaking");
        api.AddCommand<ApiSchemaCommand>("schema")
                .WithDescription("Shows the TypeScript declaration of one generated type and, by default, every type it references.")
                .WithExample("api", "schema", "Pet")
                .WithExample("api", "schema", "Pet", "--depth", "1");
        });
        config.AddBranch("gen", gen =>
        {
            gen.SetDescription("Generation commands for code and assets");
            gen.AddCommand<OpenApiCommand>("openapi")
                .WithDescription("Validates or generates TypeScript clients from OpenAPI specifications. Run it without --source to generate every client defined under gen.openapi.clients in catalyst.config.json. Use --insecure to skip TLS certificate validation when fetching from an HTTPS server with an untrusted (e.g. self-signed) certificate.")
                .WithExample("gen", "openapi", "--source", "./swagger.json")
                .WithExample("gen", "openapi", "--source", "https://api.example.com/openapi.json")
                .WithExample("gen", "openapi", "--source", "./swagger.json", "--language", "typescript", "--output", "./api.ts")
                .WithExample("gen", "openapi", "--source", "./swagger.json", "--language", "typescript", "--output", "./api.ts", "--class-name", "MyApi")
                .WithExample("gen", "openapi", "--source", "https://localhost:7036/openapi/v1.json", "--language", "typescript", "--output", "./api.ts", "--insecure")
                .WithExample("gen", "openapi");
        });
    }
}
