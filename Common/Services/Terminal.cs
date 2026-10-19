using Spectre.Console;

namespace Catalyst.Common.Services;

internal enum AnsiColor
{
    Green = 32,
    Yellow = 33,
    Red = 31,
}

/// <summary>
/// The two output streams of the CLI: results and progress go to stdout (<see cref="Out"/>), errors and
/// warnings go to stderr (<see cref="Error"/>), so a script can capture one without the other.
/// </summary>
internal static class Terminal
{
    private static IAnsiConsole? _error;

    public static IAnsiConsole Out => AnsiConsole.Console;

    /// <summary>The stderr console. Tests replace it to capture what is written there.</summary>
    public static IAnsiConsole Error
    {
        get => _error ??= CreateErrorConsole();
        set => _error = value;
    }

    /// <summary>
    /// Writes "label text" as one line. The text goes straight to the console's writer, so Spectre cannot
    /// wrap it, however long a path in it is. Only the label is colored, and only when the console supports it.
    /// </summary>
    public static void WriteLine(IAnsiConsole console, string label, string text, AnsiColor color)
    {
        string styledLabel = console.Profile.Capabilities.Ansi ? $"\u001b[{(int)color}m{label}\u001b[0m" : label;
        console.Profile.Out.Writer.WriteLine($"{styledLabel} {text}");
    }

    public static void WriteError(string? message)
    {
        WriteLine(Error, "Error:", message ?? "Unknown error", AnsiColor.Red);
    }

    /// <summary>A warning line; <paramref name="label"/> false writes the bare text (used for "Cancelled.").</summary>
    public static void WriteWarning(string message, bool label = true)
    {
        if (label)
        {
            WriteLine(Error, "Warning:", message, AnsiColor.Yellow);
        }
        else
        {
            Error.Profile.Out.Writer.WriteLine(message);
        }
    }

    private static IAnsiConsole CreateErrorConsole()
    {
        IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(Console.Error),
        });

        // Same rule as stdout in Program.cs: piped output stays on one line per message.
        if (Console.IsErrorRedirected)
        {
            console.Profile.Width = 140;
        }

        return console;
    }
}
