using Catalyst.Common.Configuration;
using Catalyst.Common.Enums;
using Catalyst.Common.Models;
using Catalyst.Common.Services;

namespace Catalyst.Commands.Gen;

/// <summary>
/// Generates (or, with check, verifies) every client configured under gen.openapi.clients. Output paths are
/// resolved relative to the config file's folder, so clients can be generated from anywhere in the repo.
/// </summary>
internal static class ConfiguredClientRunner
{
    public static async Task<List<ClientResult>> RunAsync(
        CatalystConfig config,
        string configDirectory,
        CancellationToken cancellationToken,
        bool check = false,
        string? onlyClient = null)
    {
        List<ClientResult> results = [];

        foreach (OpenApiClientConfig client in config.Gen.OpenApi.Clients)
        {
            if (!string.IsNullOrWhiteSpace(onlyClient) && !(client.Name?.Trim().Equals(onlyClient.Trim(), StringComparison.OrdinalIgnoreCase) ?? false))
            {
                continue;
            }

            results.Add(await GenerateClient(client, config, configDirectory, check, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    /// <summary>Relative file sources resolve against the config folder, matching how output works.</summary>
    internal static string ResolveSource(string source, string configDirectory)
    {
        return SourceResolver.DetermineSourceType(source) == SourceType.FilePath && !Path.IsPathRooted(source)
            ? Path.Combine(configDirectory, source)
            : source;
    }

    /// <summary>
    /// Generates one client. Whatever goes wrong fails only this client, so the rest of the batch still
    /// runs; the one exception is cancellation, which stops the whole run.
    /// </summary>
    private static async Task<ClientResult> GenerateClient(
        OpenApiClientConfig client,
        CatalystConfig config,
        string configDirectory,
        bool check,
        CancellationToken cancellationToken)
    {
        string name = client.Name?.Trim() ?? string.Empty;
        string source = client.Source?.Trim() ?? string.Empty;
        string displayName = name.Length > 0 ? name : "(unnamed)";

        try
        {
            if (name.Length == 0 || (client.Output?.Trim().Length ?? 0) == 0)
            {
                return ClientResult.Fail(displayName, source, IssueCodes.ClientInvalid, "A client entry needs both a name and an output path.");
            }

            if (source.Length == 0)
            {
                return ClientResult.Fail(name, source, IssueCodes.ClientInvalid, "No source is configured for this client. Add a source file path or URL.");
            }

            string output = Path.IsPathRooted(client.Output!)
                ? client.Output!
                : Path.Combine(configDirectory, client.Output!);

            source = ResolveSource(source, configDirectory);

            List<string> skipPaths = client.SkipPaths.Count > 0 ? client.SkipPaths : config.Gen.OpenApi.SkipPaths;

            return await ClientGenerator
                .GenerateAsync(new ClientRequest(name, source, output, name, "typescript", skipPaths, client.Insecure), check, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ClientResult.Fail(displayName, source, IssueCodes.GenerationFailed, ex.Message);
        }
    }
}
