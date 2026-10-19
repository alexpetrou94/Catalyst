using Catalyst.Commands;
using Catalyst.Common.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Catalyst.Tests;

internal sealed record CommandRun(int ExitCode, string Output, string Error);

/// <summary>
/// Runs the real command tree in-process with stdout and stderr captured. It replaces the static consoles, so
/// every test class that uses it belongs to the "AnsiConsole" collection.
/// </summary>
internal static class CommandHarness
{
    public static IAnsiConsole CreateConsole(StringWriter writer, int width = 1000)
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

    /// <summary>Runs the CLI with the given arguments, for example "gen", "openapi", "--source", ...</summary>
    public static CommandRun Run(int width, params string[] arguments)
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
            app.Configure(config => AppConfiguration.Configure(config, arguments, "0.0.0-test"));

            int exitCode = app.Run(arguments);
            return new CommandRun(exitCode, output.ToString(), error.ToString());
        }
        finally
        {
            AnsiConsole.Console = previousOut;
            Terminal.Error = previousError;
        }
    }

    public static CommandRun Run(params string[] arguments) => Run(1000, arguments);
}
