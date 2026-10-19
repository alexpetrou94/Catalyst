using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Catalyst.Common.Enums;
using Catalyst.Common.Models;
using Catalyst.Common.Services;
using Auxil.Common;

namespace Catalyst.Commands.Api;

/// <summary>Reads the old spec of <c>api diff</c>: a file, a URL, or a file at a git ref.</summary>
internal static class ApiBaseLoader
{
    private const string GitPrefix = "git:";

    public static async Task<(string? Content, string? DisplayPath, Issue? Problem)> LoadAsync(string baseSpec, ApiTarget target, bool insecure, CancellationToken cancellationToken)
    {
        if (baseSpec.StartsWith(GitPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return await LoadFromGitAsync(baseSpec[GitPrefix.Length..], target, cancellationToken).ConfigureAwait(false);
        }

        Result<SourceResolveResult> resolved = await SourceResolver.Resolve(baseSpec, insecure, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess)
        {
            string code = SourceResolver.ClassifyFailure(resolved.ErrorMessage);
            return (null, null, new Issue(code == IssueCodes.SourceNotFound ? IssueCodes.BaseNotFound : code, "Base spec: " + resolved.ErrorMessage));
        }

        return (resolved.Value!.Content, resolved.Value!.DisplayPath, null);
    }

    /// <summary>
    /// "ref:path" runs <c>git show ref:path</c> (the path is relative to the repository root); a bare "ref" means the
    /// same file as the new local source, at that ref, addressed relative to the current directory.
    /// </summary>
    private static async Task<(string? Content, string? DisplayPath, Issue? Problem)> LoadFromGitAsync(string spec, ApiTarget target, CancellationToken cancellationToken)
    {
        int separator = spec.IndexOf(':');
        string reference = separator < 0 ? spec : spec[..separator];
        string path = separator < 0 ? string.Empty : spec[(separator + 1)..];

        if (reference.Length == 0)
        {
            return (null, null, new Issue(IssueCodes.Usage, "A git base looks like git:<ref>:<path>, or git:<ref> for the same file as the source."));
        }

        if (path.Length == 0)
        {
            if (SourceResolver.DetermineSourceType(target.Source) != SourceType.FilePath)
            {
                return (null, null, new Issue(IssueCodes.Usage, $"git:{reference} needs a local file as the source; use git:{reference}:<path> instead."));
            }

            path = "./" + Path.GetRelativePath(Directory.GetCurrentDirectory(), Path.GetFullPath(target.Source)).Replace('\\', '/');
        }

        ProcessStartInfo startInfo = new("git")
        {
            WorkingDirectory = Directory.GetCurrentDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("show");
        startInfo.ArgumentList.Add($"{reference}:{path}");

        try
        {
            using Process process = Process.Start(startInfo)!;
            Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                string message = (await error.ConfigureAwait(false)).Trim().Split('\n')[0].Trim();
                return (null, null, new Issue(IssueCodes.BaseNotFound, $"git could not read {reference}:{path}: {message}"));
            }

            return (await output.ConfigureAwait(false), path, null);
        }
        catch (Win32Exception ex)
        {
            return (null, null, new Issue(IssueCodes.BaseNotFound, $"git is not available, so {GitPrefix}{spec} cannot be read: {ex.Message}"));
        }
    }
}
