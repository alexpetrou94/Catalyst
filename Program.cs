using System.Reflection;
using System.Text;
using Catalyst.Commands.Gen;
using Spectre.Console;
using Spectre.Console.Cli;

// Piped output (for example from the Syncfully Workbench, a script, or CI) must be portable:
// - UTF-8 text so box drawing and non-ASCII content (spec titles, descriptions) survive the
//   pipe; this must come before anything touches AnsiConsole, which captures the encoding
//   when it creates its output writer;
// - one line per message: Spectre otherwise wraps at a default 80 columns and splits paths.
//   The width is bounded so rules and tables stay readable; a path longer than the width can
//   still wrap, which consumers treat as a best-effort success line.
if (Console.IsOutputRedirected)
{
    try
    {
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    }
    catch (IOException)
    {
        // Keep the default encoding when the output handle cannot be reconfigured.
    }

    AnsiConsole.Profile.Width = 140;
}

CommandApp app = new();

app.Configure(config =>
{
    config.SetApplicationVersion(GetApplicationVersion());
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
});

return app.Run(args);

static string GetApplicationVersion()
{
    string? informationalVersion = typeof(Program).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    // Strip the source-revision suffix (for example "0.3.0+abc1234") so callers see a clean version.
    return informationalVersion?.Split('+')[0] ?? "0.0.0";
}
