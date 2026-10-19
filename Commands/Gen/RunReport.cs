using System.Text.Json;
using System.Text.Json.Serialization;
using Catalyst.Common.Models;

namespace Catalyst.Commands.Gen;

/// <summary>The modes of <c>gen openapi</c>, as written to the "mode" field of the JSON output.</summary>
internal static class RunMode
{
    public const string Validate = "validate";
    public const string Generate = "generate";
    public const string Check = "check";
}

/// <summary>What happened to one client, as written to the "status" field of the JSON output.</summary>
internal static class ClientStatus
{
    public const string Written = "written";
    public const string Unchanged = "unchanged";
    public const string UpToDate = "upToDate";
    public const string Stale = "stale";
    public const string Missing = "missing";
    public const string Failed = "failed";
}

/// <summary>The outcome for one client (the single --source client or one configured client).</summary>
internal sealed class ClientResult
{
    public string? Name { get; init; }
    public required string Source { get; init; }
    public string? OutputPath { get; set; }
    public required string Status { get; set; }
    public List<Issue> Warnings { get; } = [];
    public Issue? Error { get; set; }

    /// <summary>Paths skipped by the configured prefixes; shown as an info line, not a warning, in the text output.</summary>
    [JsonIgnore]
    public int SkippedPathCount { get; set; }

    [JsonIgnore]
    public bool Failed => Status == ClientStatus.Failed;

    public static ClientResult Fail(string? name, string source, string code, string message, string? outputPath = null)
    {
        return new ClientResult
        {
            Name = name,
            Source = source,
            OutputPath = outputPath,
            Status = ClientStatus.Failed,
            Error = new Issue(code, message),
        };
    }
}

internal sealed record SpecOperation(string Method, string Path, string? OperationId, string? Summary);

internal sealed record SpecSummary(
    string? Title,
    string? Version,
    string? Openapi,
    int PathCount,
    int SchemaCount,
    List<SpecOperation> Operations);

internal sealed record DiagnosticEntry(string Severity, string? Pointer, string Message);

/// <summary>
/// Everything a run of <c>gen openapi</c> found out. The command builds this; <see cref="ReportRenderer"/>
/// prints it as text or as the JSON document described in the README. Property order is the JSON field order.
/// </summary>
internal sealed class RunReport
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public int SchemaVersion => 1;
    public string Command => "gen openapi";
    public required string Mode { get; init; }
    public bool Success { get; set; }
    public int ExitCode { get; set; }
    public List<ClientResult> Results { get; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SpecSummary? Spec { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DiagnosticEntry>? Diagnostics { get; set; }

    public Issue? Error { get; set; }

    /// <summary>The config file a configured run read; shown in the text output only.</summary>
    [JsonIgnore]
    public string? ConfigPath { get; set; }

    /// <summary>The parsed document of a validate run, for the text rendering of the spec overview.</summary>
    [JsonIgnore]
    public (string Source, OpenApiParseResult Parsed)? Parsed { get; set; }

    [JsonIgnore]
    public bool SingleClient { get; set; }

    /// <summary>The exit code of every usage error (the conventional code for misuse of a command).</summary>
    public const int UsageExitCode = 2;

    /// <summary>The conventional exit code for a process stopped with Ctrl+C (128 + SIGINT).</summary>
    public const int CancelledExitCode = 130;

    /// <summary>Fills success and exitCode from the results and the top-level error.</summary>
    public RunReport Complete()
    {
        bool ok = Error == null && Results.All(result => Mode == RunMode.Check
            ? result.Status == ClientStatus.UpToDate
            : !result.Failed);

        Success = ok;

        if (Error?.Code == IssueCodes.Cancelled)
        {
            ExitCode = CancelledExitCode;
        }
        else if (Error?.Code == IssueCodes.Usage)
        {
            ExitCode = UsageExitCode;
        }
        else
        {
            ExitCode = ok ? 0 : 1;
        }

        return this;
    }

    public static RunReport Failure(string mode, string code, string message)
    {
        return new RunReport { Mode = mode, Error = new Issue(code, message) }.Complete();
    }
}
