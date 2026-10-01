using Catalyst.Commands.Gen;
using Spectre.Console.Cli;

CommandApp app = new();

app.Configure(config =>
{
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
