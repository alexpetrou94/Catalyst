using Catalyst.Common.Models;
using Catalyst.Common.Services;
using Auxil.Common;

namespace Catalyst.Commands.Gen;

/// <summary>What to generate for one client; the same for the single --source client and a configured one.</summary>
internal sealed record ClientRequest(
    string? Name,
    string Source,
    string Output,
    string? ClassName,
    string Language,
    List<string> SkipPaths,
    bool Insecure);

/// <summary>
/// Resolves, parses and generates one client, then writes the file or (with check) only compares it. This is
/// the one pipeline behind both <c>--source</c> runs and configured clients.
/// </summary>
internal static class ClientGenerator
{
    public static async Task<ClientResult> GenerateAsync(ClientRequest request, bool check, CancellationToken cancellationToken)
    {
        Result<SourceResolveResult> resolveResult = await SourceResolver.Resolve(request.Source, request.Insecure, cancellationToken).ConfigureAwait(false);
        if (!resolveResult.IsSuccess)
        {
            return ClientResult.Fail(request.Name, request.Source, SourceResolver.ClassifyFailure(resolveResult.ErrorMessage), resolveResult.ErrorMessage ?? "Unknown error");
        }

        Result<OpenApiParseResult> parseResult = OpenApiParser.Parse(resolveResult.Value!.Content, resolveResult.Value!.DisplayPath);
        if (!parseResult.IsSuccess)
        {
            return ClientResult.Fail(request.Name, request.Source, IssueCodes.ParseFailed, parseResult.ErrorMessage ?? "Unknown error");
        }

        if (!request.Language.Equals("typescript", StringComparison.OrdinalIgnoreCase))
        {
            return ClientResult.Fail(request.Name, request.Source, IssueCodes.UnsupportedLanguage, $"Unsupported language: {request.Language}");
        }

        List<Issue> warnings = [];
        int validationErrors = parseResult.Value!.Diagnostic.Errors.Count;
        if (validationErrors > 0)
        {
            warnings.Add(new Issue(IssueCodes.SpecValidationErrors, $"OpenAPI spec has {validationErrors} validation error(s). Generated code may be incomplete."));
        }

        TypeScriptGenerator generator = new(parseResult.Value!.Document, request.ClassName, request.SkipPaths);
        string code;
        try
        {
            code = generator.Generate();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ClientResult.Fail(request.Name, request.Source, IssueCodes.GenerationFailed, ex.Message);
        }

        warnings.AddRange(generator.Warnings);
        if (generator.SkippedPathCount > 0)
        {
            warnings.Add(new Issue(IssueCodes.SkippedPaths, $"Skipped {generator.SkippedPathCount} path(s) matching configured skip prefixes."));
        }

        string? outputPath = null;
        string status;
        try
        {
            outputPath = Path.GetFullPath(OpenApiCommand.ResolveOutputPath(request.Output, request.ClassName));

            if (check)
            {
                // Check never writes: not the file and not its folder.
                status = GeneratedFileWriter.Compare(outputPath, code) switch
                {
                    GeneratedFileWriter.FileState.Same => ClientStatus.UpToDate,
                    GeneratedFileWriter.FileState.Different => ClientStatus.Stale,
                    _ => ClientStatus.Missing,
                };
            }
            else
            {
                status = GeneratedFileWriter.WriteIfChanged(outputPath, code) ? ClientStatus.Written : ClientStatus.Unchanged;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return ClientResult.Fail(request.Name, request.Source, IssueCodes.WriteFailed, ex.Message, outputPath);
        }

        ClientResult result = new()
        {
            Name = request.Name,
            Source = request.Source,
            OutputPath = outputPath,
            Status = status,
            SkippedPathCount = generator.SkippedPathCount,
        };
        result.Warnings.AddRange(warnings);
        return result;
    }
}
