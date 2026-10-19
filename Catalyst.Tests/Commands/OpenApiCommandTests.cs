using Catalyst.Common.Configuration;
using Catalyst.Commands.Gen;
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
    public void Run_ReportsMissingSourceFile_EvenWhenItsNameLooksLikeMarkup()
    {
        // "[1]" is a Spectre style tag in markup; the error must be printed as text, not parsed.
        string source = Path.Combine(Path.GetTempPath(), "catalyst-missing", "api[1].json");

        (int exitCode, string output) = RunCommand("--source", source);

        Assert.Equal(1, exitCode);
        Assert.Contains("File not found", output);
        Assert.Contains("api[1].json", output);
    }

    private static (int ExitCode, string Output) RunCommand(params string[] arguments)
    {
        StringWriter writer = new();
        IAnsiConsole previous = AnsiConsole.Console;
        IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer),
        });
        console.Profile.Width = 1000;

        AnsiConsole.Console = console;
        try
        {
            CommandApp app = new();
            app.Configure(config =>
            {
                config.PropagateExceptions();
                config.AddCommand<OpenApiCommand>("openapi");
            });

            int exitCode = app.Run(["openapi", .. arguments]);
            return (exitCode, writer.ToString());
        }
        finally
        {
            AnsiConsole.Console = previous;
        }
    }
}
