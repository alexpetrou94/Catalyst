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
        [Description("Path to a local JSON or YAML file, or a URL, of an OpenAPI specification. Omit it to generate every client defined in catalyst.config.json.")]
        public string Source { get; init; } = string.Empty;

        [CommandOption("--language <LANGUAGE>")]
        [Description("Output language for code generation. Options: typescript. Defaults to typescript when --output is given")]
        public string? Language { get; init; }

        [CommandOption("--output <PATH>")]
        [Description("Output file or directory path. Required when --language is specified. A path with a trailing separator, an existing directory, or no file extension is a folder: the file is named after --class-name")]
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

        [CommandOption("--check")]
        [Description("Do not write anything: generate in memory and compare with the files on disk. Exit code 1 when a client is out of date or missing. Works with --source and --output, or for every configured client.")]
        public bool Check { get; init; }

        [CommandOption("--json")]
        [Description("Print one JSON document on stdout instead of text (see the README for the format). Works in every mode.")]
        public bool Json { get; init; }

        /// <summary>The language to generate: --language, else typescript whenever --output is given. Null means validate only.</summary>
        internal string? EffectiveLanguage =>
            !string.IsNullOrWhiteSpace(Language) ? Language
            : !string.IsNullOrWhiteSpace(Output) ? "typescript"
            : null;

        /// <summary>The first usage problem of these options, or null when they are fine together.</summary>
        internal string? GetUsageError()
        {
            // Spectre binds its internal "__default_command" marker as the value of an option that is last on
            // the line without a value (for example a trailing "--source"), instead of reporting it.
            foreach ((string option, string? value) in new[] { ("--source", Source), ("--language", Language), ("--output", Output), ("--class-name", ClassName) })
            {
                if (value == "__default_command")
                {
                    return $"The {option} option needs a value.";
                }
            }

            if (SkipPaths?.Contains("__default_command") == true)
            {
                return "The --skip-paths option needs a value.";
            }

            if (string.IsNullOrWhiteSpace(Source))
            {
                // Without --source the command generates every configured client, so the
                // single-client options do not apply.
                return HasSingleClientOptions()
                    ? "--language, --output, --class-name, --skip-paths and --insecure require --source. Pass --source, or omit the single-client options to generate every client from catalyst.config.json."
                    : null;
            }

            if (!string.IsNullOrWhiteSpace(Language) && string.IsNullOrWhiteSpace(Output))
            {
                return "The --output option is required when --language is specified.";
            }

            if (Check && EffectiveLanguage == null)
            {
                return "--check compares generated code with a file, so it needs --output (or use it without --source to check every configured client).";
            }

            return null;
        }

        public override ValidationResult Validate()
        {
            // With --json the usage error is reported in the JSON document (RunAsync), not by Spectre.
            string? usageError = Json ? null : GetUsageError();
            return usageError == null ? base.Validate() : ValidationResult.Error(usageError);
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
        return await RunAsync(settings, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the command: build the report of what happened, then print it as text or JSON. Cancellation
    /// (Ctrl+C) ends the run with a CANCELLED report and exit code 130.
    /// </summary>
    internal static async Task<int> RunAsync(Settings settings, CancellationToken cancellationToken)
    {
        RunReport report;
        try
        {
            report = await BuildReportAsync(settings, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            report = RunReport.Failure(ModeOf(settings), IssueCodes.Cancelled, "Cancelled.");
        }

        ReportRenderer.Render(report, settings.Json);
        return report.ExitCode;
    }

    private static string ModeOf(Settings settings)
    {
        if (settings.Check)
        {
            return RunMode.Check;
        }

        return string.IsNullOrWhiteSpace(settings.Source) || settings.EffectiveLanguage != null
            ? RunMode.Generate
            : RunMode.Validate;
    }

    private static async Task<RunReport> BuildReportAsync(Settings settings, CancellationToken cancellationToken)
    {
        string mode = ModeOf(settings);

        string? usageError = settings.GetUsageError();
        if (usageError != null)
        {
            return RunReport.Failure(mode, IssueCodes.Usage, usageError);
        }

        if (string.IsNullOrWhiteSpace(settings.Source))
        {
            return await BuildConfiguredReportAsync(mode, cancellationToken).ConfigureAwait(false);
        }

        if (mode == RunMode.Validate)
        {
            return await BuildValidationReportAsync(settings, cancellationToken).ConfigureAwait(false);
        }

        ClientRequest request = new(
            Name: null,
            Source: settings.Source,
            Output: settings.Output!,
            ClassName: settings.ClassName,
            Language: settings.EffectiveLanguage!,
            SkipPaths: ResolveSkipPaths(settings.SkipPaths),
            Insecure: settings.Insecure);

        RunReport report = new() { Mode = mode, SingleClient = true };
        report.Results.Add(await ClientGenerator.GenerateAsync(request, check: mode == RunMode.Check, cancellationToken).ConfigureAwait(false));
        return report.Complete();
    }

    /// <summary>Validation only: read and parse the spec, then describe it. Source and parse failures are top-level errors.</summary>
    private static async Task<RunReport> BuildValidationReportAsync(Settings settings, CancellationToken cancellationToken)
    {
        Result<SourceResolveResult> resolveResult = await SourceResolver.Resolve(settings.Source, settings.Insecure, cancellationToken).ConfigureAwait(false);
        if (!resolveResult.IsSuccess)
        {
            return RunReport.Failure(RunMode.Validate, SourceResolver.ClassifyFailure(resolveResult.ErrorMessage), resolveResult.ErrorMessage ?? "Unknown error");
        }

        Result<OpenApiParseResult> parseResult = OpenApiParser.Parse(resolveResult.Value!.Content, resolveResult.Value!.DisplayPath);
        if (!parseResult.IsSuccess)
        {
            return RunReport.Failure(RunMode.Validate, IssueCodes.ParseFailed, parseResult.ErrorMessage ?? "Unknown error");
        }

        OpenApiParseResult parsed = parseResult.Value!;
        RunReport report = new()
        {
            Mode = RunMode.Validate,
            Spec = SummarizeSpec(parsed),
            Diagnostics = DescribeDiagnostics(parsed.Diagnostic),
            Parsed = (resolveResult.Value!.DisplayPath, parsed),
        };

        int errorCount = parsed.Diagnostic.Errors.Count;
        if (errorCount > 0)
        {
            report.Error = new Issue(IssueCodes.SpecInvalid, $"OpenAPI spec has {errorCount} validation error(s).");
        }

        return report.Complete();
    }

    /// <summary>Generates or checks every client defined under gen.openapi.clients in the nearest catalyst.config.json.</summary>
    internal static async Task<RunReport> BuildConfiguredReportAsync(string mode, CancellationToken cancellationToken, string? clientName = null)
    {
        ConfigLoadResult loadResult = ConfigLoader.LoadDetailed(Directory.GetCurrentDirectory());

        if (loadResult.Error is not null)
        {
            return RunReport.Failure(mode, IssueCodes.ConfigInvalid, loadResult.Error);
        }

        if (loadResult.Config is null || loadResult.Path is null)
        {
            return RunReport.Failure(mode, IssueCodes.ConfigNotFound, "No catalyst.config.json found in this directory or any parent. Pass --source to generate a single client.");
        }

        if (loadResult.Config.Gen.OpenApi.Clients.Count == 0)
        {
            return RunReport.Failure(mode, IssueCodes.NoClients, $"{loadResult.Path} defines no clients under gen.openapi.clients.");
        }

        if (!string.IsNullOrWhiteSpace(clientName)
            && !loadResult.Config.Gen.OpenApi.Clients.Any(c => c.Name.Trim().Equals(clientName.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return RunReport.Failure(mode, IssueCodes.Usage, $"No client named \"{clientName}\" in {loadResult.Path}. Configured clients: {string.Join(", ", loadResult.Config.Gen.OpenApi.Clients.Select(c => c.Name))}.");
        }

        string configDirectory = Path.GetDirectoryName(loadResult.Path)!;

        RunReport report = new() { Mode = mode, ConfigPath = loadResult.Path };
        report.Results.AddRange(await ConfiguredClientRunner
            .RunAsync(loadResult.Config, configDirectory, cancellationToken, check: mode == RunMode.Check, onlyClient: clientName)
            .ConfigureAwait(false));
        return report.Complete();
    }

    private static SpecSummary SummarizeSpec(OpenApiParseResult parsed)
    {
        OpenApiDocument document = parsed.Document;
        List<SpecOperation> operations = [];

        foreach (KeyValuePair<string, IOpenApiPathItem> path in (document.Paths ?? []).OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            foreach (KeyValuePair<HttpMethod, OpenApiOperation> operation in (path.Value.Operations ?? []).OrderBy(o => o.Key.Method, StringComparer.Ordinal))
            {
                operations.Add(new SpecOperation(
                    operation.Key.Method.ToUpperInvariant(),
                    path.Key,
                    string.IsNullOrWhiteSpace(operation.Value.OperationId) ? null : operation.Value.OperationId,
                    string.IsNullOrWhiteSpace(operation.Value.Summary) ? null : operation.Value.Summary));
            }
        }

        // The reader reports the spec version as a major.minor enum ("OpenApi3_0"), not the exact text.
        string version = parsed.Diagnostic.SpecificationVersion.ToString().Replace("OpenApi", string.Empty).Replace('_', '.');

        return new SpecSummary(
            document.Info?.Title,
            document.Info?.Version,
            version,
            document.Paths?.Count ?? 0,
            document.Components?.Schemas?.Count ?? 0,
            operations);
    }

    private static List<DiagnosticEntry> DescribeDiagnostics(OpenApiDiagnostic diagnostic)
    {
        return diagnostic.Errors.Select(e => new DiagnosticEntry("error", e.Pointer, e.Message))
            .Concat(diagnostic.Warnings.Select(w => new DiagnosticEntry("warning", w.Pointer, w.Message)))
            .ToList();
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

    /// <summary>
    /// The one output rule, for the CLI and for configured clients: a trailing separator, an existing
    /// directory, or a path without a file extension is a folder (the file is named after the class or
    /// client name); anything else is the file itself.
    /// </summary>
    internal static string ResolveOutputPath(string? output, string? className)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return string.Empty;
        }

        bool looksLikeDirectory = output.EndsWith(Path.DirectorySeparatorChar)
            || output.EndsWith(Path.AltDirectorySeparatorChar)
            || Directory.Exists(output)
            || !Path.HasExtension(output);

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
}
