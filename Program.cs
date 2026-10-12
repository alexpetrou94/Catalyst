using System.Reflection;
using Catalyst.Commands.Gen;
using Spectre.Console;
using Spectre.Console.Cli;

// Piped output (for example from the Syncfully Workbench or a script) must stay on one
// line per message; Spectre otherwise wraps at a default 80 columns and splits paths.
// The width stays bounded because an unbounded one makes Spectre allocate huge buffers.
if (Console.IsOutputRedirected)
{
    AnsiConsole.Profile.Width = 4096;
}

CommandApp app = new();

app.Configure(config =>
{
    config.SetApplicationVersion(GetApplicationVersion());
    config.AddBranch("gen", gen =>
    {
        gen.SetDescription("Generation commands for code and assets");
        gen.AddCommand<OpenApiCommand>("openapi")
            .WithDescription("Deserializes and validates an OpenAPI specification for future code generation. Use --insecure to skip TLS certificate validation when fetching from an HTTPS server with an untrusted (e.g. self-signed) certificate.")
            .WithExample("gen", "openapi", "--source", "./swagger.json")
            .WithExample("gen", "openapi", "--source", "https://api.example.com/openapi.json")
            .WithExample("gen", "openapi", "--source", "./swagger.json", "--language", "typescript", "--output", "./api.ts")
            .WithExample("gen", "openapi", "--source", "./swagger.json", "--language", "typescript", "--output", "./api.ts", "--class-name", "MyApi")
            .WithExample("gen", "openapi", "--source", "https://localhost:7036/openapi/v1.json", "--language", "typescript", "--output", "./api.ts", "--insecure");
    });
});

return app.Run(args);

static string GetApplicationVersion()
{
    string? informationalVersion = typeof(Program).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    // Strip the source-revision suffix (for example "0.3.0+abc1234") so callers see a clean version.
    return informationalVersion?.Split('+')[0] ?? "0.0.0";
}
