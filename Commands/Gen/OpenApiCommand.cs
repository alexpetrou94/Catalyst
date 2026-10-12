using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using Catalyst.Common.Configuration;
using Catalyst.Common.Models;
using Catalyst.Common.Services;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Spectre.Console;
using Spectre.Console.Cli;
using Auxil.Common;

namespace Catalyst.Commands.Gen;

[Description("Deserializes and validates an OpenAPI specification for future code generation")]
internal sealed class OpenApiCommand : AsyncCommand<OpenApiCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandOption("--source <SOURCE>")]
        [Description("Path to a local JSON file or a URL to an OpenAPI specification. Omit it to generate every client defined in catalyst.config.json.")]
        public string Source { get; init; } = string.Empty;

        [CommandOption("--language <LANGUAGE>")]
        [Description("Output language for code generation. Options: typescript")]
        public string? Language { get; init; }

        [CommandOption("--output <PATH>")]
        [Description("Output file or directory path. Required when --language is specified. When a directory is given, the file is named after --class-name")]
        public string? Output { get; init; }

        [CommandOption("--class-name <NAME>")]
        [Description("Generated client name. Defaults to the API title from the OpenAPI spec")]
        public string? ClassName { get; init; }

        [CommandOption("--skip-paths <PATHS>")]
        [Description("Path prefixes to skip during generation (e.g. /api/auth/). Repeatable or comma-separated. Overrides catalyst.config.json.")]
        public string[]? SkipPaths { get; init; }

        [CommandOption("--insecure")]
        [Description("Skip TLS certificate validation when fetching a remote OpenAPI spec over HTTPS. Only use with a local server that has an untrusted (e.g. self-signed) certificate. Validation stays enabled by default.")]
        public bool Insecure { get; init; }

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Source))
            {
                // Without --source the command generates every configured client, so the
                // single-client options do not apply.
                if (HasSingleClientOptions())
                {
                    return ValidationResult.Error("--language, --output, --class-name, --skip-paths and --insecure require --source. Pass --source, or omit the single-client options to generate every client from catalyst.config.json.");
                }

                return base.Validate();
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

        private bool HasSingleClientOptions()
        {
            return !string.IsNullOrWhiteSpace(Language)
                || !string.IsNullOrWhiteSpace(Output)
                || !string.IsNullOrWhiteSpace(ClassName)
                || SkipPaths is { Length: > 0 }
                || Insecure;
        }
    }

    protected override async Task<int> ExecuteAsync([NotNull] CommandContext context, [NotNull] Settings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.Source))
        {
            return await ExecuteConfiguredClientsAsync(cancellationToken).ConfigureAwait(false);
        }

        Result<SourceResolveResult> resolveResult = await SourceResolver.Resolve(settings.Source, settings.Insecure, cancellationToken).ConfigureAwait(false);
        if (!resolveResult.IsSuccess)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {resolveResult.ErrorMessage}");
            return 1;
        }

        Result<OpenApiParseResult> parseResult = OpenApiParser.Parse(resolveResult.Value!.Content);
        if (!parseResult.IsSuccess)
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

    /// <summary>
    /// Generates every client defined under gen.openapi.clients in the nearest catalyst.config.json.
    /// Exit code 0 only when every configured client was written.
    /// </summary>
    private static async Task<int> ExecuteConfiguredClientsAsync(CancellationToken cancellationToken)
    {
        ConfigLoadResult loadResult = ConfigLoader.LoadDetailed(Directory.GetCurrentDirectory());

        if (loadResult.Error is not null)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(loadResult.Error)}");
            return 1;
        }

        if (loadResult.Config is null || loadResult.Path is null)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] No catalyst.config.json found in this directory or any parent. Pass --source to generate a single client.");
            return 1;
        }

        if (loadResult.Config.Gen.OpenApi.Clients.Count == 0)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(loadResult.Path)} defines no clients under gen.openapi.clients.");
            return 1;
        }

        string configDirectory = Path.GetDirectoryName(loadResult.Path)!;

        AnsiConsole.MarkupLine($"[grey]Generating {loadResult.Config.Gen.OpenApi.Clients.Count} client(s) from[/] {Markup.Escape(loadResult.Path)}");

        List<ConfiguredClientOutcome> outcomes = await ConfiguredClientRunner
            .RunAsync(loadResult.Config, configDirectory, cancellationToken)
            .ConfigureAwait(false);

        foreach (ConfiguredClientOutcome outcome in outcomes)
        {
            if (outcome.Success)
            {
                AnsiConsole.MarkupLine($"[green]TypeScript generated:[/] {Markup.Escape(outcome.OutputPath!)}");

                foreach (string warning in outcome.Warnings)
                {
                    AnsiConsole.MarkupLine($"[yellow]Warning:[/] {Markup.Escape(warning)}");
                }
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(outcome.Name)}: {Markup.Escape(outcome.Error ?? "Unknown error")}");
            }
        }

        int failed = outcomes.Count((outcome) => !outcome.Success);

        if (failed == 0)
        {
            AnsiConsole.MarkupLine($"[green]{outcomes.Count} client(s) generated.[/]");
            return 0;
        }

        AnsiConsole.MarkupLine($"[red]{failed} of {outcomes.Count} client(s) failed.[/]");
        return 1;
    }

    internal static List<string> ResolveSkipPaths(string[]? cliSkipPaths, CatalystConfig? config = null)
    {
        if (cliSkipPaths is { Length: > 0 })
        {
            List<string> expanded = [];
            foreach (string raw in cliSkipPaths)
            {
                foreach (string part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    expanded.Add(part);
                }
            }

            return expanded;
        }

        CatalystConfig? resolved = config ?? ConfigLoader.Load();
        List<string>? configSkip = resolved?.Gen?.OpenApi?.SkipPaths;

        if (configSkip is { Count: > 0 })
        {
            return configSkip;
        }

        return [];
    }

    internal static string ResolveOutputPath(string? output, string? className)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return string.Empty;
        }

        bool looksLikeDirectory = output.EndsWith(Path.DirectorySeparatorChar)
            || output.EndsWith(Path.AltDirectorySeparatorChar)
            || Directory.Exists(output);

        if (!looksLikeDirectory)
        {
            return output;
        }

        string fileName = string.IsNullOrWhiteSpace(className)
            ? "api.ts"
            : $"{SanitizeFileName(className)}.ts";

        return Path.Combine(output, fileName);
    }

    private static string SanitizeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "api" : sanitized;
    }

    private static int ExecuteGeneration(Settings settings, OpenApiDocument document)
    {
        if (settings.Language!.Equals("typescript", StringComparison.OrdinalIgnoreCase))
        {
            List<string> skipPaths = ResolveSkipPaths(settings.SkipPaths);
            TypeScriptGenerator generator = new TypeScriptGenerator(document, settings.ClassName, skipPaths);
            string code = generator.Generate();

            string outputPath = ResolveOutputPath(settings.Output, settings.ClassName);
            string? directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(outputPath, code);

            if (generator.SkippedPathCount > 0)
            {
                AnsiConsole.MarkupLine($"[grey]Skipped {generator.SkippedPathCount} path(s) matching configured skip prefixes.[/]");
            }

            foreach (string warning in generator.DuplicateWarnings)
            {
                AnsiConsole.MarkupLine($"[yellow]Warning:[/] {Markup.Escape(warning)}");
            }

            AnsiConsole.MarkupLine($"[green]TypeScript generated:[/] {Markup.Escape(outputPath)}");
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

        Table infoTable = new Table()
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

            if (Console.IsOutputRedirected)
            {
                RenderPathList(document);
            }
            else
            {
                RenderPathTree(document);
            }
        }

        RenderDiagnostics(diagnostic);
    }

    /// <summary>
    /// One line per operation for piped output: a tree costs three lines per endpoint and is
    /// mostly indentation in a log or an output panel.
    /// </summary>
    private static void RenderPathList(OpenApiDocument document)
    {
        AnsiConsole.MarkupLine($"[bold]Paths ({document.Paths!.Count})[/]");

        foreach (KeyValuePair<string, IOpenApiPathItem> path in document.Paths.OrderBy(p => p.Key))
        {
            if (path.Value.Operations == null)
            {
                continue;
            }

            foreach (KeyValuePair<HttpMethod, OpenApiOperation> operation in path.Value.Operations.OrderBy(o => o.Key.Method))
            {
                string method = operation.Key.Method.ToUpperInvariant().PadRight(6);
                AnsiConsole.MarkupLine($"  {method} {Markup.Escape(path.Key)}");
            }
        }
    }

    private static void RenderPathTree(OpenApiDocument document)
    {
        Tree tree = new Tree("[bold]Paths[/]")
            .Style(new Style(Color.Blue));

        foreach (KeyValuePair<string, IOpenApiPathItem> path in document.Paths!.OrderBy(p => p.Key))
        {
            TreeNode pathNode = tree.AddNode($"[yellow]{Markup.Escape(path.Key)}[/]");

            if (path.Value.Operations != null)
            {
                foreach (KeyValuePair<HttpMethod, OpenApiOperation> operation in path.Value.Operations.OrderBy(o => o.Key.Method))
                {
                    Color methodColor = operation.Key.Method.ToUpperInvariant() switch
                    {
                        "GET" => Color.Green,
                        "POST" => Color.Blue,
                        "PUT" => Color.Yellow,
                        "DELETE" => Color.Red,
                        "PATCH" => Color.Magenta1,
                        _ => Color.Grey,
                    };

                    string summary = !string.IsNullOrWhiteSpace(operation.Value.Summary)
                        ? $" - {Markup.Escape(operation.Value.Summary)}"
                        : string.Empty;

                    pathNode.AddNode($"[{methodColor}]{operation.Key.Method}[/]{summary}");
                }
            }
        }

        AnsiConsole.Write(tree);
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
        Table diagTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Yellow)
            .AddColumn("Severity", c => c.Width(12))
            .AddColumn("Pointer")
            .AddColumn("Message");

        foreach (OpenApiError error in errors)
        {
            diagTable.AddRow(
                "[red]Error[/]",
                Markup.Escape(error.Pointer ?? "N/A"),
                Markup.Escape(error.Message));
        }

        foreach (OpenApiError warning in warnings)
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
