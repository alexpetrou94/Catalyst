using System.Net.Http;
using System.Text.Json;
using Catalyst.Common.Models;
using Catalyst.Common.Services;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Spectre.Console;

namespace Catalyst.Commands.Gen;

/// <summary>Prints a <see cref="RunReport"/> as human text or as the JSON document of the --json contract.</summary>
internal static class ReportRenderer
{
    public static void Render(RunReport report, bool json)
    {
        if (json)
        {
            WriteJson(report);
        }
        else
        {
            WriteText(report);
        }
    }

    /// <summary>One JSON document on stdout, nothing else. The text goes to the console's raw writer.</summary>
    private static void WriteJson(RunReport report)
    {
        Terminal.Out.Profile.Out.Writer.WriteLine(JsonSerializer.Serialize(report, RunReport.JsonOptions));
    }

    private static void WriteText(RunReport report)
    {
        if (report.Error != null)
        {
            WriteTopLevelError(report);
            return;
        }

        if (report.Mode == RunMode.Validate)
        {
            WriteValidationSuccess(report);
            return;
        }

        bool check = report.Mode == RunMode.Check;

        if (!report.SingleClient && report.ConfigPath != null)
        {
            string verb = check ? "Checking" : "Generating";
            AnsiConsole.MarkupLine($"[grey]{verb} {report.Results.Count} client(s) from[/] {Markup.Escape(report.ConfigPath)}");
        }

        foreach (ClientResult result in report.Results)
        {
            WriteClient(result, check, report.SingleClient);
        }

        WriteSummary(report, check);
    }

    private static void WriteTopLevelError(RunReport report)
    {
        Issue error = report.Error!;

        if (error.Code == IssueCodes.Cancelled)
        {
            Terminal.WriteWarning("Cancelled.", label: false);
        }
        else if (error.Code == IssueCodes.SpecInvalid && report.Parsed is { } parsed)
        {
            RenderOutput(parsed.Source, parsed.Parsed.Document, parsed.Parsed.Diagnostic);
            AnsiConsole.WriteLine();
            Terminal.Error.MarkupLine("[red]Validation failed with errors.[/]");
        }
        else
        {
            Terminal.WriteError(error.Message);
        }
    }

    private static void WriteValidationSuccess(RunReport report)
    {
        (string source, OpenApiParseResult parsed) = report.Parsed!.Value;
        RenderOutput(source, parsed.Document, parsed.Diagnostic);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[green]Document parsed and validated successfully.[/]");
        AnsiConsole.MarkupLine("[grey]Ready for code generation.[/]");
    }

    private static void WriteClient(ClientResult result, bool check, bool singleClient)
    {
        if (result.Failed)
        {
            Terminal.WriteError(singleClient ? result.Error!.Message : $"{result.Name}: {result.Error!.Message}");
            return;
        }

        List<Issue> warnings = result.Warnings.Where(w => w.Code != IssueCodes.SkippedPaths).ToList();

        if (singleClient && result.SkippedPathCount > 0)
        {
            AnsiConsole.MarkupLine($"[grey]Skipped {result.SkippedPathCount} path(s) matching configured skip prefixes.[/]");
        }

        // A single run lists warnings before its result line; a configured run keeps each client's warnings after it.
        if (singleClient)
        {
            WriteWarnings(warnings);
        }

        if (check)
        {
            WriteCheckLine(result);
        }
        else
        {
            WriteGeneratedLine(result);
        }

        if (!singleClient)
        {
            WriteWarnings(warnings);
        }
    }

    private static void WriteWarnings(List<Issue> warnings)
    {
        foreach (Issue warning in warnings)
        {
            Terminal.WriteWarning(warning.Message);
        }
    }

    /// <summary>
    /// The "TypeScript generated: &lt;path&gt;" line is a contract with tools that parse the output (the Syncfully
    /// Workbench), so it is identical whether or not the file was rewritten; an unchanged file only adds an info
    /// line after it. Plain text, never wrapped: a path split over two lines would hide it from those tools.
    /// </summary>
    private static void WriteGeneratedLine(ClientResult result)
    {
        Terminal.WriteLine(Terminal.Out, "TypeScript generated:", result.OutputPath!, AnsiColor.Green);

        if (result.Status == ClientStatus.Unchanged)
        {
            AnsiConsole.MarkupLine("[grey]Unchanged since the last generation; file not rewritten.[/]");
        }
    }

    private static void WriteCheckLine(ClientResult result)
    {
        (string label, AnsiColor color) = result.Status switch
        {
            ClientStatus.UpToDate => ("Up to date:", AnsiColor.Green),
            ClientStatus.Stale => ("Out of date:", AnsiColor.Yellow),
            _ => ("Missing:", AnsiColor.Yellow),
        };

        Terminal.WriteLine(Terminal.Out, label, result.OutputPath!, color);
    }

    private static void WriteSummary(RunReport report, bool check)
    {
        int total = report.Results.Count;

        if (check)
        {
            int notUpToDate = report.Results.Count(r => r.Status != ClientStatus.UpToDate);
            AnsiConsole.MarkupLine(notUpToDate == 0
                ? $"[green]{total} client(s) up to date.[/]"
                : $"[yellow]{notUpToDate} of {total} client(s) out of date, missing or failed.[/]");
            return;
        }

        if (report.SingleClient)
        {
            return;
        }

        int failed = report.Results.Count(r => r.Failed);
        if (failed == 0)
        {
            AnsiConsole.MarkupLine($"[green]{total} client(s) generated.[/]");
        }
        else
        {
            Terminal.Error.MarkupLine($"[red]{failed} of {total} client(s) failed.[/]");
        }
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
