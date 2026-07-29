using Catalyst.Commands.Gen;
using Spectre.Console.Cli;

CommandApp app = new();

app.Configure(config =>
{
    config.AddBranch("gen", gen =>
    {
        gen.SetDescription("Generation commands for code and assets");
        gen.AddCommand<OpenApiCommand>("openapi")
            .WithDescription("Deserializes and validates an OpenAPI specification for future code generation")
            .WithExample("gen", "openapi", "--source", "./swagger.json")
            .WithExample("gen", "openapi", "--source", "https://api.example.com/openapi.json")
            .WithExample("gen", "openapi", "--source", "./swagger.json", "--language", "typescript", "--output", "./api.ts")
            .WithExample("gen", "openapi", "--source", "./swagger.json", "--language", "typescript", "--output", "./api.ts", "--class-name", "MyApi");
    });
});

return app.Run(args);
