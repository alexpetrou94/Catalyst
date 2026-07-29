using Catalyst.Common.Enums;
using Catalyst.Common.Models;
using UPhoricLibrary.Common;

namespace Catalyst.Common.Services;

internal static class SourceResolver
{
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(30);

    public static SourceType DetermineSourceType(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return SourceType.Unknown;
        }

        if (Uri.TryCreate(source, UriKind.Absolute, out Uri? uriResult))
        {
            if (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps)
            {
                return SourceType.Url;
            }
        }

        return SourceType.FilePath;
    }

    public static async Task<Result<SourceResolveResult>> Resolve(string source, CancellationToken cancellationToken)
    {
        SourceType type = DetermineSourceType(source);

        if (type == SourceType.Url)
        {
            return await ResolveUrl(source, cancellationToken).ConfigureAwait(false);
        }

        if (type == SourceType.FilePath)
        {
            return ResolveFile(source);
        }

        return Result<SourceResolveResult>.Error($"Unrecognized source type: {source}");
    }

    private static async Task<Result<SourceResolveResult>> ResolveUrl(string url, CancellationToken cancellationToken)
    {
        try
        {
            using HttpClient client = new HttpClient();
            client.Timeout = HttpTimeout;
            client.DefaultRequestHeaders.Add("User-Agent", "Catalyst-CLI/1.0");
            string content = await client.GetStringAsync(url, cancellationToken).ConfigureAwait(false);

            return Result<SourceResolveResult>.Ok(new SourceResolveResult
            {
                Content = content,
                DisplayPath = url,
                Type = SourceType.Url,
            });
        }
        catch (HttpRequestException ex)
        {
            return Result<SourceResolveResult>.Error($"Failed to fetch URL: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return Result<SourceResolveResult>.Error("Request timed out after 30 seconds.");
        }
        catch (Exception ex)
        {
            return Result<SourceResolveResult>.Error($"Unexpected error while fetching URL: {ex.Message}");
        }
    }

    private static Result<SourceResolveResult> ResolveFile(string path)
    {
        string fullPath = Path.GetFullPath(path);

        if (!File.Exists(fullPath))
        {
            return Result<SourceResolveResult>.Error($"File not found: {fullPath}");
        }

        try
        {
            string content = File.ReadAllText(fullPath);

            return Result<SourceResolveResult>.Ok(new SourceResolveResult
            {
                Content = content,
                DisplayPath = fullPath,
                Type = SourceType.FilePath,
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result<SourceResolveResult>.Error($"Access denied reading file: {ex.Message}");
        }
        catch (IOException ex)
        {
            return Result<SourceResolveResult>.Error($"IO error reading file: {ex.Message}");
        }
        catch (Exception ex)
        {
            return Result<SourceResolveResult>.Error($"Unexpected error reading file: {ex.Message}");
        }
    }
}
