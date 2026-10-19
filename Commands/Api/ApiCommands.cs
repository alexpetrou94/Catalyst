using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Catalyst.Commands.Gen;
using Catalyst.Common.Configuration;
using Catalyst.Common.Models;
using Catalyst.Common.Services;
using Spectre.Console.Cli;
using Auxil.Common;

namespace Catalyst.Commands.Api;

/// <summary>Where the spec of an <c>api</c> command comes from, and how its client would be generated.</summary>
internal sealed record ApiTarget(string Source, string? ClassName, List<string> SkipPaths, bool Insecure);

/// <summary>Options shared by every <c>api</c> command.</summary>
internal class ApiSettings : CommandSettings
{
    [CommandOption("--source <SOURCE>")]
    [Description("Path to a local JSON or YAML file, or a URL, of an OpenAPI specification. Without --source or --client the only client in catalyst.config.json is used.")]
    public string? Source { get; init; }

    [CommandOption("--client <NAME>")]
    [Description("Name of a client in catalyst.config.json: its source, skip paths and name are used, so the output matches the generated client.")]
    public string? Client { get; init; }

    [CommandOption("--insecure")]
    [Description("Skip TLS certificate validation when fetching a remote spec over HTTPS (local servers with self-signed certificates only).")]
    public bool Insecure { get; init; }

    [CommandOption("--json")]
    [Description("Print one JSON document on stdout instead of text (see the README).")]
    public bool Json { get; init; }
}

internal sealed class ApiListCommand : AsyncCommand<ApiListCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandOption("--filter <TEXT>")]
        [Description("Only operations matching every word: path, operationId, method name, summary or tags (case-insensitive).")]
        public string? Filter { get; init; }

        [CommandOption("--tag <TAG>")]
        [Description("Only operations with this tag.")]
        public string? Tag { get; init; }

        [CommandOption("--method <VERB>")]
        [Description("Only operations with this HTTP method (get, post, ...).")]
        public string? Method { get; init; }
    }

    protected override Task<int> ExecuteAsync([NotNull] CommandContext context, [NotNull] Settings settings, CancellationToken cancellationToken)
    {
        return ApiCommandRunner.RunAsync(settings, "api list", model =>
        {
            List<ApiListItem> items = ApiLookup.FilterOperations(model, settings.Filter, settings.Tag, settings.Method)
                .Select(o => new ApiListItem(o.Method, o.Path, o.OperationId, o.Summary, o.Deprecated, o.Tags, o.Call))
                .ToList();

            return new ApiReport { Command = "api list", Operations = items }.Complete();
        }, cancellationToken);
    }
}

internal sealed class ApiShowCommand : AsyncCommand<ApiShowCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandArgument(0, "<operation>")]
        [Description("\"GET /items/{id}\", an operationId, or a generated call such as get.itemsId or client.get.itemsId.")]
        public string[] Operation { get; init; } = [];

        [CommandOption("--no-types")]
        [Description("Do not print the TypeScript declarations of the types the operation uses.")]
        public bool NoTypes { get; init; }
    }

    protected override Task<int> ExecuteAsync([NotNull] CommandContext context, [NotNull] Settings settings, CancellationToken cancellationToken)
    {
        return ApiCommandRunner.RunAsync(settings, "api show", model =>
        {
            string selector = string.Join(' ', settings.Operation);
            OperationMatch match = ApiLookup.FindOperation(model, selector);

            if (match.Operation == null)
            {
                return ApiReport.Failure("api show", match.ErrorCode!, match.Message!, match.Suggestions);
            }

            return new ApiReport
            {
                Command = "api show",
                Operation = match.Operation,
                Types = settings.NoTypes ? null : ApiLookup.TypeClosure(model, match.Operation.ReferencedTypes),
            }.Complete();
        }, cancellationToken);
    }
}

internal sealed class ApiDiffCommand : AsyncCommand<ApiDiffCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandOption("--base <BASE>")]
        [Description("The old spec to compare with: a path, a URL, or git:<ref>:<path> (read with git show). With a file source, git:<ref> alone means the same file at that ref, e.g. git:HEAD.")]
        public string? Base { get; init; }

        [CommandOption("--fail-on-breaking")]
        [Description("Exit with code 1 when there is any breaking change (the default exit code is 0).")]
        public bool FailOnBreaking { get; init; }

        public override Spectre.Console.ValidationResult Validate()
        {
            return string.IsNullOrWhiteSpace(Base) ? Spectre.Console.ValidationResult.Error("The --base option is required: the old spec to compare with.") : base.Validate();
        }
    }

    protected override Task<int> ExecuteAsync([NotNull] CommandContext context, [NotNull] Settings settings, CancellationToken cancellationToken)
    {
        return ApiCommandRunner.RunAsync(settings, "api diff", async (model, target, token) =>
        {
            (string? content, string? displayPath, Issue? problem) = await ApiBaseLoader.LoadAsync(settings.Base!, target, settings.Insecure, token).ConfigureAwait(false);
            if (content == null)
            {
                return ApiReport.Failure("api diff", problem!.Code, problem.Message);
            }

            (ClientModel? baseModel, Issue? baseProblem) = ApiCommandRunner.BuildModel(content, displayPath!, target);
            if (baseModel == null)
            {
                return ApiReport.Failure("api diff", baseProblem!.Code, "Base spec: " + baseProblem.Message);
            }

            List<ApiChange> changes = ApiDiff.Compare(baseModel, model);
            int breaking = changes.Count(c => c.Severity == ChangeSeverity.Breaking);
            ApiReport report = new() { Command = "api diff", Changes = changes, Summary = new DiffSummary(breaking, changes.Count - breaking) };
            report.Complete();

            if (settings.FailOnBreaking && breaking > 0)
            {
                report.Success = false;
                report.ExitCode = 1;
            }

            return report;
        }, cancellationToken);
    }
}

internal sealed class ApiSchemaCommand : AsyncCommand<ApiSchemaCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandArgument(0, "<name>")]
        [Description("A generated type name or the original component name.")]
        public string Name { get; init; } = string.Empty;

        [CommandOption("--depth <N>")]
        [Description("How many levels of referenced types to include (0 is the type alone). Default: all.")]
        public int? Depth { get; init; }

        public override Spectre.Console.ValidationResult Validate()
        {
            return Depth < 0 ? Spectre.Console.ValidationResult.Error("--depth cannot be negative.") : base.Validate();
        }
    }

    protected override Task<int> ExecuteAsync([NotNull] CommandContext context, [NotNull] Settings settings, CancellationToken cancellationToken)
    {
        return ApiCommandRunner.RunAsync(settings, "api schema", model =>
        {
            TypeDeclaration? type = ApiLookup.FindType(model, settings.Name);
            if (type == null)
            {
                return ApiReport.Failure("api schema", IssueCodes.SchemaNotFound, $"No type matches \"{settings.Name}\".", ApiLookup.SuggestTypes(model, settings.Name));
            }

            return new ApiReport { Command = "api schema", Types = ApiLookup.TypeClosure(model, [type.Name], settings.Depth) }.Complete();
        }, cancellationToken);
    }
}

/// <summary>The flow every <c>api</c> command shares: pick the spec, build the client model, answer, print.</summary>
internal static class ApiCommandRunner
{
    public static Task<int> RunAsync(ApiSettings settings, string command, Func<ClientModel, ApiReport> answer, CancellationToken cancellationToken)
    {
        return RunAsync(settings, command, (model, _, _) => Task.FromResult(answer(model)), cancellationToken);
    }

    /// <summary>Like the synchronous overload, for answers that need the target (to find a base spec) or do I/O.</summary>
    public static async Task<int> RunAsync(ApiSettings settings, string command, Func<ClientModel, ApiTarget, CancellationToken, Task<ApiReport>> answer, CancellationToken cancellationToken)
    {
        ApiReport report;

        try
        {
            report = await BuildReportAsync(settings, command, answer, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            report = ApiReport.Failure(command, IssueCodes.Cancelled, "Cancelled.");
        }

        report.Render(settings.Json);
        return report.ExitCode;
    }

    private static async Task<ApiReport> BuildReportAsync(ApiSettings settings, string command, Func<ClientModel, ApiTarget, CancellationToken, Task<ApiReport>> answer, CancellationToken cancellationToken)
    {
        (ApiTarget? target, Issue? problem) = ResolveTarget(settings);
        if (target == null)
        {
            return ApiReport.Failure(command, problem!.Code, problem.Message);
        }

        Result<SourceResolveResult> resolved = await SourceResolver.Resolve(target.Source, target.Insecure, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess)
        {
            return ApiReport.Failure(command, SourceResolver.ClassifyFailure(resolved.ErrorMessage), resolved.ErrorMessage ?? "Unknown error");
        }

        (ClientModel? model, Issue? modelProblem) = BuildModel(resolved.Value!.Content, resolved.Value!.DisplayPath, target);
        if (model == null)
        {
            return ApiReport.Failure(command, modelProblem!.Code, modelProblem.Message);
        }

        return await answer(model, target, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Parses a spec and generates the client model for it; the same pipeline for the new and the base spec.</summary>
    internal static (ClientModel? Model, Issue? Problem) BuildModel(string content, string displayPath, ApiTarget target)
    {
        Result<OpenApiParseResult> parsed = OpenApiParser.Parse(content, displayPath);
        if (!parsed.IsSuccess)
        {
            return (null, new Issue(IssueCodes.ParseFailed, parsed.ErrorMessage ?? "Unknown error"));
        }

        TypeScriptGenerator generator = new(parsed.Value!.Document, target.ClassName, target.SkipPaths);
        try
        {
            generator.Generate();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (null, new Issue(IssueCodes.GenerationFailed, ex.Message));
        }

        return (generator.Model, null);
    }

    /// <summary>
    /// --source wins as given; --client picks a configured client; with neither, the config must hold exactly one
    /// client. Anything else is a usage error that lists the configured client names.
    /// </summary>
    internal static (ApiTarget? Target, Issue? Problem) ResolveTarget(ApiSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Source))
        {
            if (!string.IsNullOrWhiteSpace(settings.Client))
            {
                return (null, new Issue(IssueCodes.Usage, "Use either --source or --client, not both."));
            }

            return (new ApiTarget(settings.Source, null, OpenApiCommand.ResolveSkipPaths(null), settings.Insecure), null);
        }

        ConfigLoadResult loaded = ConfigLoader.LoadDetailed(Directory.GetCurrentDirectory());
        if (loaded.Error is not null)
        {
            return (null, new Issue(IssueCodes.ConfigInvalid, loaded.Error));
        }

        List<OpenApiClientConfig> clients = loaded.Config?.Gen.OpenApi.Clients ?? [];
        if (loaded.Config is null || loaded.Path is null || clients.Count == 0)
        {
            return (null, new Issue(IssueCodes.Usage, "Pass --source <path|url>, or define a client in catalyst.config.json and pass --client <name>."));
        }

        OpenApiClientConfig? client;
        if (!string.IsNullOrWhiteSpace(settings.Client))
        {
            client = clients.FirstOrDefault(c => c.Name.Equals(settings.Client.Trim(), StringComparison.OrdinalIgnoreCase));
            if (client == null)
            {
                return (null, new Issue(IssueCodes.Usage, $"No client named \"{settings.Client}\" in {loaded.Path}. Configured clients: {string.Join(", ", clients.Select(c => c.Name))}."));
            }
        }
        else if (clients.Count == 1)
        {
            client = clients[0];
        }
        else
        {
            return (null, new Issue(IssueCodes.Usage, $"{loaded.Path} defines several clients: {string.Join(", ", clients.Select(c => c.Name))}. Pass --client <name> or --source."));
        }

        string source = client.Source.Trim();
        if (source.Length == 0)
        {
            return (null, new Issue(IssueCodes.ClientInvalid, $"Client \"{client.Name}\" has no source configured."));
        }

        string configDirectory = Path.GetDirectoryName(loaded.Path)!;
        List<string> skipPaths = client.SkipPaths.Count > 0 ? client.SkipPaths : loaded.Config.Gen.OpenApi.SkipPaths;

        return (new ApiTarget(ConfiguredClientRunner.ResolveSource(source, configDirectory), client.Name.Trim(), skipPaths, client.Insecure || settings.Insecure), null);
    }
}
