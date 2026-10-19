using Catalyst.Common.Configuration;
using Catalyst.Commands.Gen;
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
            string nested = string.Join('/', Enumerable.Repeat("nested-directory-segment", 5));
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

    private static string CreateTempDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), "catalyst-command-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private sealed record CommandRun(int ExitCode, string Output, string Error);

    private static IAnsiConsole CreateConsole(StringWriter writer, int width = 1000)
    {
        IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer),
        });
        console.Profile.Width = width;
        return console;
    }

    private static async Task<(int ExitCode, string Error)> CapturingErrorAsync(Func<Task<int>> action)
    {
        StringWriter error = new();
        IAnsiConsole previous = Terminal.Error;
        Terminal.Error = CreateConsole(error);
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
        StringWriter output = new();
        StringWriter error = new();
        IAnsiConsole previousOut = AnsiConsole.Console;
        IAnsiConsole previousError = Terminal.Error;

        AnsiConsole.Console = CreateConsole(output, width);
        Terminal.Error = CreateConsole(error, width);
        try
        {
            CommandApp app = new();
            app.Configure(config =>
            {
                config.PropagateExceptions();
                config.AddCommand<OpenApiCommand>("openapi");
            });

            int exitCode = app.Run(["openapi", .. arguments]);
            return new CommandRun(exitCode, output.ToString(), error.ToString());
        }
        finally
        {
            AnsiConsole.Console = previousOut;
            Terminal.Error = previousError;
        }
    }
}
