using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Catalyst.Commands.Api;
using Catalyst.Commands.Gen;
using Catalyst.Common.Configuration;
using Catalyst.Common.Models;
using Catalyst.Common.Services;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Catalyst.Commands.Mcp;

/// <summary>
/// The MCP tools of <c>catalyst mcp</c>. Each one is a thin wrapper over the code the CLI commands use and returns the
/// CLI's --json payload as structured content, with the CLI's text output as the text content. Every call is
/// independent: the config and the spec are read again each time, and nothing is shared between calls.
/// There is deliberately no insecure parameter: only a configured client's own "insecure" setting relaxes TLS validation,
/// and a source URL passed directly is always validated.
/// </summary>
[McpServerToolType]
internal static class CatalystTools
{
    private const string SelectionHelp =
        "Which API: pass `client` (a name from list_clients) or `source` (a spec file path or URL). "
        + "With neither, the only configured client is used; with several configured clients one of them is required.";

    [McpServerTool(Name = "list_clients", ReadOnly = true, Idempotent = true)]
    [Description("Lists the API clients configured in catalyst.config.json of the working directory (name, spec source, output file). "
        + "Call this first to learn the valid `client` values for the other tools. Returns an empty list when there is no config.")]
    public static CallToolResult ListClients()
    {
        ConfigLoadResult loaded = ConfigLoader.LoadDetailed(Directory.GetCurrentDirectory());

        if (loaded.Error is not null)
        {
            return Result(new { schemaVersion = 1, command = "list_clients", success = false, error = new { code = IssueCodes.ConfigInvalid, message = loaded.Error } }, loaded.Error, isError: true);
        }

        var clients = (loaded.Config?.Gen.OpenApi.Clients ?? [])
            .Select(c => new { name = c.Name, source = c.Source, output = c.Output })
            .ToList();
        string summary = clients.Count == 0
            ? "No clients configured."
            : string.Join(Environment.NewLine, clients.Select(c => $"{c.name}  source: {c.source}  output: {c.output}"));

        return Result(new { schemaVersion = 1, command = "list_clients", success = true, configPath = loaded.Path, clients, error = (object?)null }, summary, isError: false);
    }

    [McpServerTool(Name = "list_operations", ReadOnly = true, Idempotent = true)]
    [Description("Lists the operations (endpoints) of an API as the generated TypeScript client exposes them: one line each with the HTTP method, path, "
        + "the generated call (for example client.get.itemsId) and the summary. Use it to find an endpoint; then use show_operation for details. "
        + "Filters combine: every word of `filter` must match the path, operationId, method name, summary or tags (case-insensitive).")]
    public static Task<CallToolResult> ListOperations(
        [Description(SelectionHelp)] string? client = null,
        [Description("Spec file path or URL. Alternative to `client`.")] string? source = null,
        [Description("Words to match, for example \"album tracks\".")] string? filter = null,
        [Description("Only operations with this tag.")] string? tag = null,
        [Description("Only this HTTP method: get, post, put, delete, patch.")] string? method = null,
        CancellationToken cancellationToken = default)
    {
        return AnswerAsync(client, source, "api list", model => ApiAnswers.List(model, filter, tag, method), cancellationToken);
    }

    [McpServerTool(Name = "show_operation", ReadOnly = true, Idempotent = true)]
    [Description("Shows how to call one endpoint with the generated TypeScript client: the exact call signature, a parameter table "
        + "(name in the call, name on the wire, location, required, type), the body and response types with media types, and the TypeScript "
        + "declarations of every type involved. Use this instead of reading the OpenAPI spec or the generated file. "
        + "`operation` is \"GET /items/{id}\" (any parameter names), an operationId, or a generated call such as get.itemsId or client.get.itemsId. "
        + "Use list_operations when you do not know the exact name; an unknown name returns close suggestions.")]
    public static Task<CallToolResult> ShowOperation(
        [Description("\"GET /items/{id}\", an operationId, or a generated call like client.get.itemsId.")] string operation,
        [Description(SelectionHelp)] string? client = null,
        [Description("Spec file path or URL. Alternative to `client`.")] string? source = null,
        [Description("Include the TypeScript declarations of the types the operation uses (default true). Set false for a short answer.")] bool includeTypes = true,
        CancellationToken cancellationToken = default)
    {
        return AnswerAsync(client, source, "api show", model => ApiAnswers.Show(model, operation, includeTypes), cancellationToken);
    }

    [McpServerTool(Name = "show_type", ReadOnly = true, Idempotent = true)]
    [Description("Shows the TypeScript declaration of one generated type, by its generated name or its original OpenAPI component name, "
        + "together with the types it references (dependencies first). Use it when show_operation named a type you need to see in full, "
        + "or with `depth` 0 to see just that type. An unknown name returns close suggestions.")]
    public static Task<CallToolResult> ShowType(
        [Description("Generated type name or original component name, for example Pet.")] string name,
        [Description(SelectionHelp)] string? client = null,
        [Description("Spec file path or URL. Alternative to `client`.")] string? source = null,
        [Description("How many levels of referenced types to include; 0 is the type alone. Default: all.")] int? depth = null,
        CancellationToken cancellationToken = default)
    {
        if (depth < 0)
        {
            return Task.FromResult(FromApi(ApiReport.Failure("api schema", IssueCodes.Usage, "depth cannot be negative.")));
        }

        return AnswerAsync(client, source, "api schema", model => ApiAnswers.Schema(model, name, depth), cancellationToken);
    }

    [McpServerTool(Name = "diff_spec", ReadOnly = true, Idempotent = true)]
    [Description("Compares an old spec (`base`) with the current spec and reports breaking and non-breaking changes in terms of the generated client "
        + "(removed or renamed calls, parameters added or removed, changed types). Use it after changing a backend endpoint, before regenerating, "
        + "to see what breaks for the frontend. `base` is a file path, a URL, `git:<ref>:<path>` (read with git show), or `git:<ref>` meaning the same "
        + "file as the source at that ref, for example git:HEAD. It reports changes and never fails because of them.")]
    public static Task<CallToolResult> DiffSpec(
        [Description("The old spec: file path, URL, git:<ref>:<path>, or git:<ref> (same file as the source, at that ref).")] string @base,
        [Description(SelectionHelp)] string? client = null,
        [Description("The current spec: file path or URL. Alternative to `client`.")] string? source = null,
        CancellationToken cancellationToken = default)
    {
        return AnswerAsync(client, source, "api diff", (model, target, token) => ApiAnswers.DiffAsync(model, target, @base, insecure: false, failOnBreaking: false, token), cancellationToken);
    }

    [McpServerTool(Name = "generate_clients", Destructive = false, Idempotent = true)]
    [Description("Generates the TypeScript client of one configured API client, or of all of them, to the output files declared in catalyst.config.json "
        + "(it cannot write anywhere else). Use it after the backend API changed. With `check` true nothing is written: it only reports whether each client "
        + "file is up to date, out of date (stale) or missing, which is the way to verify a client matches its spec.")]
    public static async Task<CallToolResult> GenerateClients(
        [Description("Name of one configured client (see list_clients). Omit to process all configured clients.")] string? client = null,
        [Description("Only compare with the files on disk and write nothing.")] bool check = false,
        CancellationToken cancellationToken = default)
    {
        RunReport report = await OpenApiCommand
            .BuildConfiguredReportAsync(check ? RunMode.Check : RunMode.Generate, cancellationToken, client)
            .ConfigureAwait(false);

        StringBuilder summary = new();
        if (report.Error != null)
        {
            summary.AppendLine($"Error {report.Error.Code}: {report.Error.Message}");
        }

        foreach (ClientResult result in report.Results)
        {
            string detail = result.Failed ? $"{result.Error!.Code}: {result.Error.Message}" : result.OutputPath ?? string.Empty;
            summary.AppendLine($"{result.Name}  {result.Status}  {detail}");
            foreach (Issue warning in result.Warnings.Where(w => w.Code != IssueCodes.SkippedPaths))
            {
                summary.AppendLine($"  warning {warning.Code}: {warning.Message}");
            }
        }

        bool isError = report.Error != null || report.Results.Any(r => r.Failed);
        return Result(JsonSerializer.SerializeToElement(report, RunReport.JsonOptions), summary.ToString().TrimEnd(), isError);
    }

    /// <summary>Selects the spec exactly like the CLI (--client / --source / the only configured client) and answers from its model.</summary>
    private static Task<CallToolResult> AnswerAsync(string? client, string? source, string command, Func<ClientModel, ApiReport> answer, CancellationToken cancellationToken)
    {
        return AnswerAsync(client, source, command, (model, _, _) => Task.FromResult(answer(model)), cancellationToken);
    }

    private static async Task<CallToolResult> AnswerAsync(string? client, string? source, string command, Func<ClientModel, ApiTarget, CancellationToken, Task<ApiReport>> answer, CancellationToken cancellationToken)
    {
        // No insecure setting: Insecure stays false, so only a configured client's own setting applies.
        ApiSettings settings = new() { Client = client, Source = source };
        ApiReport report = await ApiCommandRunner.BuildReportAsync(settings, command, answer, cancellationToken).ConfigureAwait(false);
        return FromApi(report);
    }

    private static CallToolResult FromApi(ApiReport report)
    {
        return new CallToolResult
        {
            IsError = report.Error != null,
            Content = [new TextContentBlock { Text = report.ToText() }],
            StructuredContent = JsonDocument.Parse(report.ToJson()).RootElement.Clone(),
        };
    }

    private static CallToolResult Result(object payload, string text, bool isError)
    {
        return Result(JsonSerializer.SerializeToElement(payload, RunReport.JsonOptions), text, isError);
    }

    private static CallToolResult Result(JsonElement payload, string text, bool isError)
    {
        return new CallToolResult
        {
            IsError = isError,
            Content = [new TextContentBlock { Text = text }],
            StructuredContent = payload,
        };
    }
}
