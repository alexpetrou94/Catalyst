using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using Catalyst.Common.Models;
using Catalyst.Common.Services;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Spectre.Console;
using Spectre.Console.Cli;
using UPhoricLibrary.Common;

namespace Catalyst.Commands.Gen;

[Description("Deserializes and validates an OpenAPI specification for future code generation")]
internal sealed class OpenApiCommand : Command<OpenApiCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandOption("--source <SOURCE>")]
        [Description("Path to a local JSON file or a URL to an OpenAPI specification")]
        public string Source { get; init; } = string.Empty;

        [CommandOption("--language <LANGUAGE>")]
        [Description("Output language for code generation. Options: typescript")]
        public string? Language { get; init; }

        [CommandOption("--output <PATH>")]
        [Description("Output file path. Required when --language is specified")]
        public string? Output { get; init; }

        [CommandOption("--class-name <NAME>")]
        [Description("Generated client name. Defaults to the API title from the OpenAPI spec")]
        public string? ClassName { get; init; }

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Source))
            {
                return ValidationResult.Error("The --source option is required.");
            }

            if (!string.IsNullOrWhiteSpace(Language))
            {
                if (string.IsNullOrWhiteSpace(Output))
                {
                    return ValidationResult.Error("The --output option is required when --language is specified.");
                }
            }

            return base.Validate();
        }
    }

    protected override int Execute([NotNull] CommandContext context, [NotNull] Settings settings, CancellationToken cancellationToken)
    {
        Result<SourceResolveResult> resolveResult = SourceResolver.Resolve(settings.Source, cancellationToken);
        if (!resolveResult.Success)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {resolveResult.ErrorMessage}");
            return 1;
        }

        Result<OpenApiParseResult> parseResult = OpenApiParser.Parse(resolveResult.Value!.Content);
        if (!parseResult.Success)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {parseResult.ErrorMessage}");
            return 1;
        }

        if (!string.IsNullOrWhiteSpace(settings.Language))
        {
            if (parseResult.Value!.Diagnostic.Errors.Count > 0)
            {
                AnsiConsole.MarkupLine($"[yellow]Warning:[/] OpenAPI spec has {parseResult.Value!.Diagnostic.Errors.Count} validation error(s). Generated code may be incomplete.");
            }

            return ExecuteGeneration(settings, parseResult.Value!.Document);
        }

        return ExecuteValidation(resolveResult.Value!.DisplayPath, parseResult.Value!.Document, parseResult.Value!.Diagnostic);
    }

    private static int ExecuteGeneration(Settings settings, OpenApiDocument document)
    {
        if (settings.Language!.Equals("typescript", StringComparison.OrdinalIgnoreCase))
        {
            var generator = new TypeScriptGenerator(document, settings.ClassName);
            string code = generator.Generate();

            File.WriteAllText(settings.Output!, code);
            AnsiConsole.MarkupLine($"[green]TypeScript generated:[/] {Markup.Escape(settings.Output!)}");
            return 0;
        }

        AnsiConsole.MarkupLine($"[red]Error:[/] Unsupported language: {settings.Language}");
        return 1;
    }

    private static int ExecuteValidation(string sourceDisplay, OpenApiDocument document, OpenApiDiagnostic diagnostic)
    {
        RenderOutput(sourceDisplay, document, diagnostic);

        if (diagnostic.Errors.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[red]Validation failed with errors.[/]");
            return 1;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[green]Document parsed and validated successfully.[/]");
        AnsiConsole.MarkupLine("[grey]Ready for code generation.[/]");
        return 0;
    }

    private static void RenderOutput(string sourceDisplay, OpenApiDocument document, OpenApiDiagnostic diagnostic)
    {
        AnsiConsole.Write(new Rule("[bold blue]OpenAPI Specification[/]").RuleStyle("grey"));
        AnsiConsole.Write(new Panel(new Markup($"[bold]Source:[/] {Markup.Escape(sourceDisplay)}"))
            .Border(BoxBorder.Rounded)
            .BorderStyle(new Style(Color.Grey)));

        var infoTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumn("Property", c => c.Width(20))
            .AddColumn("Value");

        infoTable.AddRow("Title", Markup.Escape(document.Info?.Title ?? "(none)"));
        infoTable.AddRow("Version", Markup.Escape(document.Info?.Version ?? "(none)"));
        infoTable.AddRow("Paths", document.Paths?.Count.ToString() ?? "0");
        infoTable.AddRow("Schemas", document.Components?.Schemas?.Count.ToString() ?? "0");
        infoTable.AddRow("Security Schemes", document.Components?.SecuritySchemes?.Count.ToString() ?? "0");

        AnsiConsole.Write(infoTable);

        if (document.Paths?.Count > 0)
        {
            AnsiConsole.WriteLine();
            var tree = new Tree("[bold]Paths[/]")
                .Style(new Style(Color.Blue));

            foreach (KeyValuePair<string, IOpenApiPathItem> path in document.Paths.OrderBy(p => p.Key))
            {
                var pathNode = tree.AddNode($"[yellow]{Markup.Escape(path.Key)}[/]");

                if (path.Value.Operations != null)
                {
                    foreach (KeyValuePair<HttpMethod, OpenApiOperation> operation in path.Value.Operations.OrderBy(o => o.Key.Method))
                    {
                        var methodColor = operation.Key.Method.ToUpperInvariant() switch
                        {
                            "GET" => Color.Green,
                            "POST" => Color.Blue,
                            "PUT" => Color.Yellow,
                            "DELETE" => Color.Red,
                            "PATCH" => Color.Magenta1,
                            _ => Color.Grey,
                        };

                        var summary = !string.IsNullOrWhiteSpace(operation.Value.Summary)
                            ? $" - {Markup.Escape(operation.Value.Summary)}"
                            : string.Empty;

                        pathNode.AddNode($"[{methodColor}]{operation.Key.Method}[/]{summary}");
                    }
                }
            }

            AnsiConsole.Write(tree);
        }

        RenderDiagnostics(diagnostic);
    }

    private static void RenderDiagnostics(OpenApiDiagnostic diagnostic)
    {
        List<OpenApiError> errors = diagnostic.Errors?.ToList() ?? [];
        List<OpenApiError> warnings = diagnostic.Warnings?.ToList() ?? [];

        if (errors.Count == 0 && warnings.Count == 0)
        {
            return;
        }

        AnsiConsole.WriteLine();
        var diagTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Yellow)
            .AddColumn("Severity", c => c.Width(12))
            .AddColumn("Pointer")
            .AddColumn("Message");

        foreach (var error in errors)
        {
            diagTable.AddRow(
                "[red]Error[/]",
                Markup.Escape(error.Pointer ?? "N/A"),
                Markup.Escape(error.Message));
        }

        foreach (var warning in warnings)
        {
            diagTable.AddRow(
                "[yellow]Warning[/]",
                Markup.Escape(warning.Pointer ?? "N/A"),
                Markup.Escape(warning.Message));
        }

        AnsiConsole.Write(new Panel(diagTable)
            .Header("[bold yellow]Validation Diagnostics[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Yellow));
    }
}
