using Catalyst.Common.Configuration;
using Catalyst.Commands.Gen;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Catalyst.Tests.Commands;

// The tests that run the command replace the static AnsiConsole, so they must not overlap.
[Collection("AnsiConsole")]
public class OpenApiCommandTests
{
    private static CatalystConfig ConfigWith(params string[] skipPaths) => new CatalystConfig
    {
        Gen = new GenConfig
        {
            OpenApi = new OpenApiGenConfig
            {
                SkipPaths = skipPaths.ToList()
            }
        }
    };

    [Fact]
    public void Insecure_DefaultsToFalse_SoCertificateValidationStaysOn()
    {
        OpenApiCommand.Settings settings = new();

        Assert.False(settings.Insecure);
    }

    [Fact]
    public void ResolveSkipPaths_UsesCli_WhenProvided()
    {
        List<string> result = OpenApiCommand.ResolveSkipPaths(["/cli/"], ConfigWith("/cfg/"));

        Assert.Equal(["/cli/"], result);
    }

    [Fact]
    public void ResolveSkipPaths_UsesConfig_WhenCliAbsent()
    {
        List<string> result = OpenApiCommand.ResolveSkipPaths(null, ConfigWith("/cfg/"));

        Assert.Equal(["/cfg/"], result);
    }

    [Fact]
    public void ResolveSkipPaths_ReturnsEmpty_WhenNeitherProvided()
    {
        List<string> result = OpenApiCommand.ResolveSkipPaths(null, null);

        Assert.Empty(result);
    }

    [Fact]
    public void ResolveSkipPaths_SplitsCommaSeparatedCliValues()
    {
        List<string> result = OpenApiCommand.ResolveSkipPaths(["/a/, /b/"], null);

        Assert.Equal(["/a/", "/b/"], result);
    }

    [Fact]
    public void ResolveSkipPaths_SupportsRepeatableCliValues()
    {
        List<string> result = OpenApiCommand.ResolveSkipPaths(["/a/", "/b/"], null);

        Assert.Equal(["/a/", "/b/"], result);
    }

    [Fact]
    public void ResolveOutputPath_UsesClassName_WhenOutputEndsWithSeparator()
    {
        char separator = Path.DirectorySeparatorChar;
        string result = OpenApiCommand.ResolveOutputPath($"src{separator}external-apis{separator}", "CommunicationApi");

        Assert.Equal(Path.Combine($"src{separator}external-apis", "CommunicationApi.ts"), result);
    }

    [Fact]
    public void ResolveOutputPath_DefaultsToApi_WhenOutputIsDirectoryAndNoClassName()
    {
        char separator = Path.DirectorySeparatorChar;
        string result = OpenApiCommand.ResolveOutputPath($"src{separator}external-apis{separator}", null);

        Assert.Equal(Path.Combine($"src{separator}external-apis", "api.ts"), result);
    }

    [Fact]
    public void ResolveOutputPath_TreatsExistingDirectoryAsDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string result = OpenApiCommand.ResolveOutputPath(dir, "CommunicationApi");

            Assert.Equal(Path.Combine(dir, "CommunicationApi.ts"), result);
        }
        finally
        {
            Directory.Delete(dir);
        }
    }

    [Fact]
    public void ResolveOutputPath_PassesThroughPlainFilePath()
    {
        string result = OpenApiCommand.ResolveOutputPath("src\\external-apis\\api.ts", "CommunicationApi");

        Assert.Equal("src\\external-apis\\api.ts", result);
    }

    [Fact]
    public void ResolveOutputPath_SanitizesInvalidFileNameCharacters()
    {
        char separator = Path.DirectorySeparatorChar;

        // '/' is invalid in file names on both Windows and Unix.
        string result = OpenApiCommand.ResolveOutputPath($"out{separator}", "My/Api");

        Assert.Equal(Path.Combine("out", "My_Api.ts"), result);
    }

    [Fact]
    public void ResolveOutputPath_ReturnsEmpty_WhenOutputIsBlank()
    {
        Assert.Equal(string.Empty, OpenApiCommand.ResolveOutputPath("", null));
        Assert.Equal(string.Empty, OpenApiCommand.ResolveOutputPath("   ", null));
    }

    [Fact]
    public void Validate_AllowsMissingSource_ForConfigDrivenGeneration()
    {
        Assert.True(new OpenApiCommand.Settings().Validate().Successful);
    }

    [Fact]
    public void Validate_RejectsSingleClientOptions_WithoutSource()
    {
        Assert.False(new OpenApiCommand.Settings { Language = "typescript", Output = "api.ts" }.Validate().Successful);
        Assert.False(new OpenApiCommand.Settings { ClassName = "Api" }.Validate().Successful);
        Assert.False(new OpenApiCommand.Settings { SkipPaths = ["/x/"] }.Validate().Successful);
        Assert.False(new OpenApiCommand.Settings { Insecure = true }.Validate().Successful);
    }

    [Fact]
    public void Validate_RequiresOutput_WhenLanguageIsGivenWithSource()
    {
        Assert.False(new OpenApiCommand.Settings { Source = "spec.json", Language = "typescript" }.Validate().Successful);
        Assert.True(new OpenApiCommand.Settings { Source = "spec.json", Language = "typescript", Output = "api.ts" }.Validate().Successful);
    }

    [Fact]
    public void ResolveOutputPath_TreatsAPathWithoutExtensionAsAFolder()
    {
        string result = OpenApiCommand.ResolveOutputPath(Path.Combine("src", "clients"), "Api");

        Assert.Equal(Path.Combine("src", "clients", "Api.ts"), result);
    }

    [Fact]
    public void EffectiveLanguage_DefaultsToTypeScript_WhenOutputIsGiven()
    {
        Assert.Equal("typescript", new OpenApiCommand.Settings { Source = "spec.json", Output = "api.ts" }.EffectiveLanguage);
        Assert.Equal("typescript", new OpenApiCommand.Settings { Source = "spec.json", Language = "typescript", Output = "api.ts" }.EffectiveLanguage);
        Assert.Null(new OpenApiCommand.Settings { Source = "spec.json" }.EffectiveLanguage);
    }

    [Fact]
    public void Run_ReportsMissingSourceFile_ToStderr_EvenWhenItsNameLooksLikeMarkup()
    {
        // "[1]" is a Spectre style tag in markup; the error must be printed as text, not parsed.
        string source = Path.Combine(Path.GetTempPath(), "catalyst-missing", "api[1].json");

        CommandRun run = RunCommand("--source", source);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("File not found", run.Error);
        Assert.Contains("api[1].json", run.Error);
        Assert.DoesNotContain("File not found", run.Output);
    }

    [Fact]
    public void Run_ReportsWhatIsWrongWithAMalformedSpec()
    {
        string dir = CreateTempDirectory();
        try
        {
            string spec = Path.Combine(dir, "broken.json");
            File.WriteAllText(spec, "{ \"openapi\": \"3.0.1\", \"info\": ");

            CommandRun run = RunCommand("--source", spec);

            Assert.Equal(1, run.ExitCode);
            Assert.Contains("Failed to parse OpenAPI document", run.Error);
            Assert.DoesNotContain("empty result", run.Error);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Run_GeneratesTypeScript_WhenOnlyOutputIsGiven_AndLeavesUnchangedFilesAlone()
    {
        string dir = CreateTempDirectory();
        try
        {
            string spec = Path.Combine(dir, "spec.json");
            string output = Path.Combine(dir, "out", "api.ts");
            File.Copy(TestPaths.SpecPath("minimal"), spec);

            CommandRun first = RunCommand("--source", spec, "--output", output);

            Assert.Equal(0, first.ExitCode);
            Assert.Contains("TypeScript generated", first.Output);
            Assert.True(File.Exists(output));

            // An identical regeneration must not rewrite the file (no timestamp churn).
            DateTime old = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(output, old);

            CommandRun second = RunCommand("--source", spec, "--output", output);

            Assert.Equal(0, second.ExitCode);
            Assert.Contains("Unchanged since the last generation", second.Output);
            Assert.Equal(old, File.GetLastWriteTimeUtc(output));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// The Syncfully Workbench finds the generated file with this exact pattern (electron/main/catalyst.ts).
    /// The line must match, and carry nothing but the path, whether or not the file was rewritten.
    /// </summary>
    [Fact]
    public void Run_SuccessLine_MatchesTheWorkbenchContract_WhenWrittenAndWhenUnchanged()
    {
        Regex workbench = new(@"^[ \t]*TypeScript generated:[ \t]*(.+?)[ \t]*$", RegexOptions.Multiline);
        string dir = CreateTempDirectory();
        try
        {
            string spec = Path.Combine(dir, "spec.json");

            // Longer than 200 characters (but under Windows MAX_PATH) and written with mixed separators:
            // the line must not wrap, and the path must come back normalised.
            // Segments are added until the full path passes 200 characters, whatever the length of the temp directory.
            const string segment = "nested-directory-segment";
            int segments = 1;
            while (Path.GetFullPath($"{dir}/{string.Join('/', Enumerable.Repeat(segment, segments))}{Path.DirectorySeparatorChar}api.ts").Length <= 200)
            {
                segments++;
            }

            string nested = string.Join('/', Enumerable.Repeat(segment, segments));
            string output = Path.GetFullPath($"{dir}/{nested}{Path.DirectorySeparatorChar}api.ts");
            Assert.InRange(output.Length, 201, 259);
            File.Copy(TestPaths.SpecPath("minimal"), spec);

            string written = RunCommand(80, "--source", spec, "--output", $"{dir}/{nested}{Path.DirectorySeparatorChar}api.ts").Output;
            string unchanged = RunCommand(80, "--source", spec, "--output", $"{dir}/{nested}{Path.DirectorySeparatorChar}api.ts").Output;

            Assert.DoesNotContain("Unchanged since", written);
            Assert.Contains("Unchanged since", unchanged);
            foreach (string stdout in new[] { written, unchanged })
            {
                // JavaScript ends a line at CR for $, .NET does not: compare with LF line ends.
                Match match = workbench.Match(stdout.Replace("\r\n", "\n"));
                Assert.True(match.Success, stdout);
                Assert.Equal(output, match.Groups[1].Value);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// On GitHub Actions Spectre reports ANSI support even though the output is redirected. The lines that tools parse
    /// must stay free of escape codes then, or "TypeScript generated:" and the other labels would no longer match.
    /// </summary>
    [Fact]
    public void Run_KeepsEscapeCodesOutOfTheParsedLines_EvenWhenTheConsoleReportsAnsi()
    {
        string dir = CreateTempDirectory();
        try
        {
            string spec = Path.Combine(dir, "spec.json");
            string output = Path.Combine(dir, "api.ts");
            File.Copy(TestPaths.SpecPath("edge-cases"), spec);

            CommandRun generated = CommandHarness.Run(1000, ansi: true, ["gen", "openapi", "--source", spec, "--output", output]);
            File.WriteAllText(output, "// old");
            CommandRun stale = CommandHarness.Run(1000, ansi: true, ["gen", "openapi", "--source", spec, "--output", output, "--check"]);
            CommandRun failed = CommandHarness.Run(1000, ansi: true, ["gen", "openapi", "--source", Path.Combine(dir, "missing.json")]);

            Assert.Contains("TypeScript generated: " + output, generated.Output);
            Assert.Contains("Warning: ", generated.Error);
            Assert.Contains("Out of date: " + output, stale.Output);
            Assert.Contains("Error: File not found", failed.Error);
            foreach (string text in new[] { generated.Output, generated.Error, stale.Output, failed.Error })
            {
                foreach (string line in text.Split('\n').Where(l => l.Contains("TypeScript generated:") || l.Contains("Out of date:") || l.Contains("Warning:") || l.Contains("Error:")))
                {
                    Assert.DoesNotContain('\u001b', line);
                }
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Run_SendsWarningsToStderr_AndResultsToStdout()
    {
        string dir = CreateTempDirectory();
        try
        {
            string spec = Path.Combine(dir, "spec.json");
            File.Copy(TestPaths.SpecPath("edge-cases"), spec);

            CommandRun run = RunCommand("--source", spec, "--output", Path.Combine(dir, "api.ts"));

            Assert.Equal(0, run.ExitCode);
            Assert.Contains("TypeScript generated", run.Output);
            Assert.DoesNotContain("Warning:", run.Output);
            Assert.Contains("Warning:", run.Error);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_ReportsCancelled_WithExitCode130_NotATimeout()
    {
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        (int exitCode, string error) = await CapturingErrorAsync(() =>
            OpenApiCommand.RunAsync(new OpenApiCommand.Settings { Source = "http://127.0.0.1:1/openapi.json" }, cancellation.Token));

        Assert.Equal(130, exitCode);
        Assert.Contains("Cancelled.", error);
        Assert.DoesNotContain("timed out", error);
    }

    // ---- --check ----

    [Fact]
    public void Check_RejectsValidationOnlyMode()
    {
        Assert.False(new OpenApiCommand.Settings { Source = "spec.json", Check = true }.Validate().Successful);
        Assert.True(new OpenApiCommand.Settings { Source = "spec.json", Check = true, Output = "api.ts" }.Validate().Successful);
        Assert.True(new OpenApiCommand.Settings { Check = true }.Validate().Successful);
    }

    [Fact]
    public void Check_ExitsZero_WhenTheFileIsUpToDate()
    {
        string dir = CreateTempDirectory();
        try
        {
            string spec = Path.Combine(dir, "spec.json");
            string output = Path.Combine(dir, "api.ts");
            File.Copy(TestPaths.SpecPath("minimal"), spec);
            RunCommand("--source", spec, "--output", output);

            CommandRun run = RunCommand("--source", spec, "--output", output, "--check");

            Assert.Equal(0, run.ExitCode);
            Assert.Contains("Up to date: " + output, run.Output);
            Assert.Contains("1 client(s) up to date.", run.Output);
            Assert.Equal(string.Empty, run.Error);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Check_ExitsOne_AndLeavesAStaleFileUntouched()
    {
        string dir = CreateTempDirectory();
        try
        {
            string spec = Path.Combine(dir, "spec.json");
            string output = Path.Combine(dir, "api.ts");
            File.Copy(TestPaths.SpecPath("minimal"), spec);
            File.WriteAllText(output, "// hand edited");
            DateTime old = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(output, old);

            CommandRun run = RunCommand("--source", spec, "--output", output, "--check");

            Assert.Equal(1, run.ExitCode);
            Assert.Contains("Out of date: " + output, run.Output);
            Assert.Equal("// hand edited", File.ReadAllText(output));
            Assert.Equal(old, File.GetLastWriteTimeUtc(output));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Check_ExitsOne_AndCreatesNothing_WhenTheFileIsMissing()
    {
        string dir = CreateTempDirectory();
        try
        {
            string spec = Path.Combine(dir, "spec.json");
            string output = Path.Combine(dir, "not-yet", "api.ts");
            File.Copy(TestPaths.SpecPath("minimal"), spec);

            CommandRun run = RunCommand("--source", spec, "--output", output, "--check");

            Assert.Equal(1, run.ExitCode);
            Assert.Contains("Missing: " + output, run.Output);
            Assert.False(Directory.Exists(Path.Combine(dir, "not-yet")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---- --json ----

    [Fact]
    public void Json_Validate_DescribesTheSpec_InTheDocumentedShape()
    {
        CommandRun run = RunCommand("--source", TestPaths.SpecPath("minimal"), "--json");

        // Parsing with unmapped members disallowed means the README shape and the output cannot drift apart silently.
        JsonReport report = ParseReport(run);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(1, report.SchemaVersion);
        Assert.Equal("gen openapi", report.Command);
        Assert.Equal("validate", report.Mode);
        Assert.True(report.Success);
        Assert.Equal(0, report.ExitCode);
        Assert.Empty(report.Results);
        Assert.Null(report.Error);
        Assert.Equal("Minimal API", report.Spec!.Title);
        Assert.Equal("1.0.0", report.Spec.Version);
        Assert.Equal("3.0", report.Spec.Openapi);
        Assert.Equal(10, report.Spec.PathCount);
        Assert.Equal(1, report.Spec.SchemaCount);
        Assert.Contains(report.Spec.Operations, o => o.Method == "GET" && o.Path == "/widgets" && o.Summary == "List widgets" && o.OperationId == null);
        Assert.Empty(report.Diagnostics!);
    }

    [Fact]
    public void Json_Generate_ReportsStatusWarningsAndTheNormalisedPath()
    {
        string dir = CreateTempDirectory();
        try
        {
            string spec = Path.Combine(dir, "spec.json");
            string output = Path.Combine(dir, "out", "api.ts");
            File.Copy(TestPaths.SpecPath("edge-cases"), spec);
            string messy = dir + "/out/../out/api.ts";

            JsonReport first = ParseReport(RunCommand("--source", spec, "--output", messy, "--json"));
            JsonReport second = ParseReport(RunCommand("--source", spec, "--output", messy, "--json"));

            Assert.Equal("generate", first.Mode);
            Assert.True(first.Success);
            JsonResult result = Assert.Single(first.Results);
            Assert.Null(result.Name);
            Assert.Equal(spec, result.Source);
            Assert.Equal(output, result.OutputPath);
            Assert.Equal("written", result.Status);
            Assert.Contains(result.Warnings, w => w.Code == "PATH_PARAMETER_MISMATCH");
            Assert.Null(result.Error);
            Assert.Equal("unchanged", Assert.Single(second.Results).Status);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Json_Check_ReportsStaleAndExitCodeOne()
    {
        string dir = CreateTempDirectory();
        try
        {
            string spec = Path.Combine(dir, "spec.json");
            string output = Path.Combine(dir, "api.ts");
            File.Copy(TestPaths.SpecPath("minimal"), spec);
            File.WriteAllText(output, "// old");

            CommandRun run = RunCommand("--source", spec, "--output", output, "--check", "--json");
            JsonReport report = ParseReport(run);

            Assert.Equal(1, run.ExitCode);
            Assert.Equal("check", report.Mode);
            Assert.False(report.Success);
            Assert.Equal(1, report.ExitCode);
            Assert.Equal("stale", Assert.Single(report.Results).Status);
            Assert.Null(report.Error);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Json_Configured_ReportsEveryClient_AndCodesForTheFailedOne()
    {
        string dir = CreateTempDirectory();
        string previous = Directory.GetCurrentDirectory();
        try
        {
            File.Copy(TestPaths.SpecPath("minimal"), Path.Combine(dir, "spec.json"));
            File.WriteAllText(Path.Combine(dir, "catalyst.config.json"), """
                { "gen": { "openapi": { "clients": [
                  { "name": "Works", "source": "spec.json", "output": "out" },
                  { "name": "NoSource", "output": "out" },
                  { "name": "Missing", "source": "gone.json", "output": "out" }
                ] } } }
                """);
            Directory.SetCurrentDirectory(dir);

            CommandRun run = RunCommand("--json");
            JsonReport report = ParseReport(run);

            Assert.Equal(1, run.ExitCode);
            Assert.Equal("generate", report.Mode);
            Assert.False(report.Success);
            Assert.Equal(["written", "failed", "failed"], report.Results.Select(r => r.Status));
            Assert.Equal(Path.Combine(dir, "out", "Works.ts"), report.Results[0].OutputPath);
            Assert.Equal("CLIENT_INVALID", report.Results[1].Error!.Code);
            Assert.Equal("SOURCE_NOT_FOUND", report.Results[2].Error!.Code);
            Assert.Null(report.Error);
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Json_ReportsErrorsAsCodes_WithNothingOnStderr()
    {
        string source = Path.Combine(Path.GetTempPath(), "catalyst-missing", "api[1].json");

        CommandRun run = RunCommand("--source", source, "--json");
        JsonReport report = ParseReport(run);

        Assert.Equal(1, run.ExitCode);
        Assert.False(report.Success);
        Assert.Equal("SOURCE_NOT_FOUND", report.Error!.Code);
        Assert.Contains("api[1].json", report.Error.Message);
    }

    [Fact]
    public void Json_ReportsUsageErrors_WithExitCodeTwo()
    {
        CommandRun run = RunCommand("--output", "api.ts", "--json");
        JsonReport report = ParseReport(run);

        Assert.Equal(2, run.ExitCode);
        Assert.Equal("USAGE", report.Error!.Code);
        Assert.Equal(2, report.ExitCode);
        Assert.False(report.Success);
    }

    [Fact]
    public void Json_ReportsAMistypedOption_AsAUsageError()
    {
        // Spectre's parser rejects this before the command runs; --json still gets its document.
        CommandRun run = RunCommand("--source", TestPaths.SpecPath("minimal"), "--outptu", "x.ts", "--json");
        JsonReport report = ParseReport(run);

        Assert.Equal(2, run.ExitCode);
        Assert.Equal("USAGE", report.Error!.Code);
        Assert.Contains("outptu", report.Error.Message);
    }

    [Fact]
    public void UsageErrors_ExitWithTwo_AndWriteNothing()
    {
        string dir = CreateTempDirectory();
        try
        {
            string spec = Path.Combine(dir, "spec.json");
            string output = Path.Combine(dir, "x.ts");
            File.Copy(TestPaths.SpecPath("minimal"), spec);

            CommandRun typo = RunCommand("--source", spec, "--outptu", output);
            CommandRun stray = RunCommand("--source", spec, "--output", output, "stray-argument");
            CommandRun incompatible = RunCommand("--output", output);
            CommandRun missingValue = RunCommand("--source");

            foreach (CommandRun run in new[] { typo, stray, incompatible, missingValue })
            {
                Assert.Equal(2, run.ExitCode);
                Assert.NotEqual(string.Empty, run.Error);
                Assert.Equal(string.Empty, run.Output);
            }

            Assert.Contains("outptu", typo.Error);
            Assert.Contains("stray-argument", stray.Error);
            Assert.False(File.Exists(output));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static JsonReport ParseReport(CommandRun run)
    {
        Assert.Equal(string.Empty, run.Error);

        return JsonSerializer.Deserialize<JsonReport>(run.Output, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        })!;
    }

    // The documented JSON shape (see the README), spelled out independently of the production types.
    private sealed record JsonIssue(string Code, string Message);

    private sealed record JsonResult(string? Name, string Source, string? OutputPath, string Status, List<JsonIssue> Warnings, JsonIssue? Error);

    private sealed record JsonOperation(string Method, string Path, string? OperationId, string? Summary);

    private sealed record JsonSpec(string Title, string Version, string Openapi, int PathCount, int SchemaCount, List<JsonOperation> Operations);

    private sealed record JsonDiagnostic(string Severity, string? Pointer, string Message);

    private sealed record JsonReport(
        int SchemaVersion,
        string Command,
        string Mode,
        bool Success,
        int ExitCode,
        List<JsonResult> Results,
        JsonSpec? Spec,
        List<JsonDiagnostic>? Diagnostics,
        JsonIssue? Error);

    private static string CreateTempDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), "catalyst-command-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static async Task<(int ExitCode, string Error)> CapturingErrorAsync(Func<Task<int>> action)
    {
        StringWriter error = new();
        IAnsiConsole previous = Terminal.Error;
        Terminal.Error = CommandHarness.CreateConsole(error);
        try
        {
            int exitCode = await action();
            return (exitCode, error.ToString());
        }
        finally
        {
            Terminal.Error = previous;
        }
    }

    private static CommandRun RunCommand(params string[] arguments) => RunCommand(1000, arguments);

    private static CommandRun RunCommand(int width, params string[] arguments)
    {
        return CommandHarness.Run(width, ["gen", "openapi", .. arguments]);
    }
}
