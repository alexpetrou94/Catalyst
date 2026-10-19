using Catalyst.Common.Configuration;
using Catalyst.Common.Enums;
using Catalyst.Common.Models;
using Catalyst.Common.Services;
using Auxil.Common;

namespace Catalyst.Commands.Gen;

/// <summary>Result of generating one client from the config file.</summary>
internal sealed record ConfiguredClientOutcome(
    string Name,
    bool Success,
    string? OutputPath,
    string? Error,
    IReadOnlyList<string> Warnings,
    bool Unchanged = false);

/// <summary>
/// Generates every client configured under gen.openapi.clients. Output paths are resolved
/// relative to the config file's folder, so clients can be generated from anywhere in the repo.
/// </summary>
internal static class ConfiguredClientRunner
{
    public static async Task<List<ConfiguredClientOutcome>> RunAsync(
        CatalystConfig config,
        string configDirectory,
        CancellationToken cancellationToken)
    {
        List<ConfiguredClientOutcome> outcomes = [];

        foreach (OpenApiClientConfig client in config.Gen.OpenApi.Clients)
        {
            outcomes.Add(await GenerateClient(client, config, configDirectory, cancellationToken).ConfigureAwait(false));
        }

        return outcomes;
    }

    /// <summary>
    /// Generates one client. Whatever goes wrong fails only this client, so the rest of the batch still
    /// runs; the one exception is cancellation, which stops the whole run.
    /// </summary>
    private static async Task<ConfiguredClientOutcome> GenerateClient(
        OpenApiClientConfig client,
        CatalystConfig config,
        string configDirectory,
        CancellationToken cancellationToken)
    {
        try
        {
            return await GenerateClientCore(client, config, configDirectory, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            string name = client.Name?.Trim() ?? string.Empty;
            return new ConfiguredClientOutcome(name.Length > 0 ? name : "(unnamed)", false, null, ex.Message, []);
        }
    }

    private static async Task<ConfiguredClientOutcome> GenerateClientCore(
        OpenApiClientConfig client,
        CatalystConfig config,
        string configDirectory,
        CancellationToken cancellationToken)
    {
        string name = client.Name.Trim();
        string source = client.Source.Trim();

        if (name.Length == 0 || client.Output.Trim().Length == 0)
        {
            return new ConfiguredClientOutcome(
                name.Length > 0 ? name : "(unnamed)",
                false,
                null,
                "A client entry needs both a name and an output path.",
                []);
        }

        if (source.Length == 0)
        {
            return new ConfiguredClientOutcome(
                name,
                false,
                null,
                "No source is configured for this client. Add a source file path or URL.",
                []);
        }

        string combinedOutput = Path.IsPathRooted(client.Output)
            ? client.Output
            : Path.Combine(configDirectory, client.Output);

        string outputPath = OpenApiCommand.ResolveOutputPath(combinedOutput, name);

        // Relative file sources resolve against the config folder, matching how output works.
        if (SourceResolver.DetermineSourceType(source) == SourceType.FilePath && !Path.IsPathRooted(source))
        {
            source = Path.Combine(configDirectory, source);
        }

        Result<SourceResolveResult> resolveResult = await SourceResolver.Resolve(source, client.Insecure, cancellationToken)
            .ConfigureAwait(false);
        if (!resolveResult.IsSuccess)
        {
            return new ConfiguredClientOutcome(name, false, null, resolveResult.ErrorMessage, []);
        }

        Result<OpenApiParseResult> parseResult = OpenApiParser.Parse(resolveResult.Value!.Content, resolveResult.Value!.DisplayPath);
        if (!parseResult.IsSuccess)
        {
            return new ConfiguredClientOutcome(name, false, null, parseResult.ErrorMessage, []);
        }

        List<string> skipPaths = client.SkipPaths.Count > 0 ? client.SkipPaths : config.Gen.OpenApi.SkipPaths;
        TypeScriptGenerator generator = new(parseResult.Value!.Document, name, skipPaths);
        string code = generator.Generate();

        bool written = GeneratedFileWriter.WriteIfChanged(outputPath, code);

        List<string> warnings = [];
        if (parseResult.Value!.Diagnostic.Errors.Count > 0)
        {
            warnings.Add($"OpenAPI spec has {parseResult.Value!.Diagnostic.Errors.Count} validation error(s). Generated code may be incomplete.");
        }

        warnings.AddRange(generator.Warnings);

        return new ConfiguredClientOutcome(name, true, outputPath, null, warnings, Unchanged: !written);
    }
}
