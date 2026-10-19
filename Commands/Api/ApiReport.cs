using System.Text.Json;
using System.Text.Json.Serialization;
using Catalyst.Commands.Gen;
using Catalyst.Common.Models;
using Catalyst.Common.Services;

namespace Catalyst.Commands.Api;

/// <summary>One line of <c>api list</c>.</summary>
internal sealed record DiffSummary(int Breaking, int NonBreaking);

internal sealed record ApiListItem(string Method, string Path, string? OperationId, string? Summary, bool Deprecated, List<string> Tags, string Call);

/// <summary>
/// The result of an <c>api</c> command: the envelope of the gen openapi JSON document (schemaVersion, command,
/// success, exitCode, error) with the payload of the command. Property order is the JSON field order.
/// </summary>
internal sealed class ApiReport
{
    /// <summary>Like the gen openapi options, but quotes in the TypeScript text stay readable instead of becoming ".</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(RunReport.JsonOptions)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public int SchemaVersion => 1;
    public required string Command { get; init; }
    public bool Success { get; set; }
    public int ExitCode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ApiListItem>? Operations { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ApiOperation? Operation { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<TypeDeclaration>? Types { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ApiChange>? Changes { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DiffSummary? Summary { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Suggestions { get; set; }

    public Issue? Error { get; set; }

    public ApiReport Complete()
    {
        Success = Error == null;
        ExitCode = Error?.Code switch
        {
            null => 0,
            IssueCodes.Usage => RunReport.UsageExitCode,
            IssueCodes.Cancelled => RunReport.CancelledExitCode,
            _ => 1,
        };

        return this;
    }

    public static ApiReport Failure(string command, string code, string message, List<string>? suggestions = null)
    {
        return new ApiReport { Command = command, Error = new Issue(code, message), Suggestions = suggestions is { Count: > 0 } ? suggestions : null }.Complete();
    }

    /// <summary>The JSON document of the --json output (and of the MCP structured content).</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>The text the CLI prints, as one string; errors are included as "Error: ..." lines.</summary>
    public string ToText()
    {
        List<string> lines = [];
        WriteText(lines.Add, lines.Add, message => lines.Add("Error: " + message));
        return string.Join(Environment.NewLine, lines);
    }

    public void Render(bool json)
    {
        if (json)
        {
            Terminal.WriteRaw(ToJson());
        }
        else
        {
            WriteText(Terminal.WriteRaw, Terminal.WriteRawError, Terminal.WriteError);
        }
    }

    private void WriteText(Action<string> write, Action<string> writeRawError, Action<string> writeError)
    {
        if (Error != null)
        {
            if (Error.Code == IssueCodes.Cancelled)
            {
                writeRawError("Cancelled.");
                return;
            }

            writeError(Error.Message);

            if (Suggestions != null)
            {
                writeRawError("Did you mean:");
                foreach (string suggestion in Suggestions)
                {
                    writeRawError("  " + suggestion);
                }
            }

            return;
        }

        if (Operations != null)
        {
            WriteList(Operations, write);
        }
        else if (Operation != null)
        {
            WriteOperation(Operation, write);
        }
        else if (Changes != null)
        {
            WriteChanges(Changes, write);
        }

        if (Types is { Count: > 0 })
        {
            if (Operation != null)
            {
                write(string.Empty);
                write("Types:");
            }

            for (int i = 0; i < Types.Count; i++)
            {
                if (i > 0)
                {
                    write(string.Empty);
                }

                write(Types[i].Declaration);
            }
        }
    }

    private static void WriteChanges(List<ApiChange> changes, Action<string> write)
    {
        if (changes.Count == 0)
        {
            write("No changes.");
            return;
        }

        List<ApiChange> breaking = changes.Where(c => c.Severity == ChangeSeverity.Breaking).ToList();
        List<ApiChange> other = changes.Where(c => c.Severity != ChangeSeverity.Breaking).ToList();

        if (breaking.Count > 0)
        {
            write($"Breaking changes ({breaking.Count})");
            foreach (ApiChange change in breaking)
            {
                write(ChangeLine("BREAKING", change));
            }
        }

        if (other.Count > 0)
        {
            write($"Other changes ({other.Count})");
            foreach (ApiChange change in other)
            {
                write(ChangeLine("change", change));
            }
        }

        write(string.Empty);
        write($"{breaking.Count} breaking, {other.Count} non-breaking change(s).");
    }

    private static string ChangeLine(string label, ApiChange change)
    {
        string subject = change.Operation != null
            ? $"{change.Operation.Method} {change.Operation.Path}  {change.Operation.Call}"
            : $"type {change.Type}";

        return $"{label.PadRight(8)}  {subject}: {change.Message}";
    }

    private static void WriteList(List<ApiListItem> items, Action<string> write)
    {
        int methodWidth = items.Count == 0 ? 0 : items.Max(i => i.Method.Length);
        int pathWidth = items.Count == 0 ? 0 : items.Max(i => i.Path.Length);
        int callWidth = items.Count == 0 ? 0 : items.Max(i => i.Call.Length);

        foreach (ApiListItem item in items)
        {
            string deprecated = item.Deprecated ? " [deprecated]" : string.Empty;
            string summary = string.IsNullOrWhiteSpace(item.Summary) ? string.Empty : " - " + OneLine(item.Summary, 100);
            write($"{item.Method.PadRight(methodWidth)}  {item.Path.PadRight(pathWidth)}  {item.Call.PadRight(callWidth)}{deprecated}{summary}".TrimEnd());
        }
    }

    private static void WriteOperation(ApiOperation operation, Action<string> write)
    {
        write($"{operation.Method} {operation.Path}{(operation.Deprecated ? " [deprecated]" : string.Empty)}");

        if (!string.IsNullOrWhiteSpace(operation.Summary))
        {
            write(OneLine(operation.Summary, 200));
        }

        if (!string.IsNullOrWhiteSpace(operation.Description))
        {
            write(OneLine(operation.Description, 600));
        }

        write(string.Empty);
        write("Call:");
        write($"  client.{operation.Verb}.{operation.Signature}");

        if (operation.Parameters.Count > 0)
        {
            write(string.Empty);
            write("Parameters:");

            string[][] rows = new[] { new[] { "NAME", "WIRE", "IN", "REQUIRED", "TYPE" } }
                .Concat(operation.Parameters.Select(p => new[] { p.Name, p.WireName, p.In, p.Required ? "required" : "optional", p.Type }))
                .ToArray();
            int[] widths = Enumerable.Range(0, 5).Select(column => rows.Max(row => row[column].Length)).ToArray();

            for (int i = 0; i < rows.Length; i++)
            {
                string cells = string.Join("  ", rows[i].Select((cell, column) => cell.PadRight(widths[column])));
                string? parameterDescription = i == 0 ? "DESCRIPTION" : operation.Parameters[i - 1].Description;
                string description = string.IsNullOrWhiteSpace(parameterDescription) ? string.Empty : "  " + OneLine(parameterDescription, 100);
                write(("  " + cells + description).TrimEnd());
            }
        }

        write(string.Empty);

        if (operation.Body != null)
        {
            string mediaType = string.IsNullOrEmpty(operation.Body.MediaType) ? string.Empty : operation.Body.MediaType + ", ";
            write($"Body: {operation.Body.Type} ({mediaType}{(operation.Body.Required ? "required" : "optional")})");
        }

        string responseMedia = string.IsNullOrEmpty(operation.Response.MediaType) ? string.Empty : $" ({operation.Response.MediaType})";
        write($"Response: {operation.Response.Type}{responseMedia}");
    }

    /// <summary>Collapses whitespace so descriptions stay on one line, and cuts them at a length.</summary>
    private static string OneLine(string text, int maxLength)
    {
        string collapsed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= maxLength ? collapsed : collapsed[..(maxLength - 3)] + "...";
    }
}
