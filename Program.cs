using System.Reflection;
using System.Text;
using Catalyst.Commands;
using Spectre.Console;
using Spectre.Console.Cli;

// Piped output (for example from the Syncfully Workbench, a script, or CI) must be portable:
// - UTF-8 text so box drawing and non-ASCII content (spec titles, descriptions) survive the
//   pipe; this must come before anything touches AnsiConsole, which captures the encoding
//   when it creates its output writer;
// - one line per message: Spectre otherwise wraps at a default 80 columns and splits paths.
//   The width is bounded so rules and tables stay readable; a path longer than the width can
//   still wrap, which consumers treat as a best-effort success line.
if (Console.IsOutputRedirected || Console.IsErrorRedirected)
{
    try
    {
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    }
    catch (IOException)
    {
        // Keep the default encoding when the output handle cannot be reconfigured.
    }

    if (Console.IsOutputRedirected)
    {
        AnsiConsole.Profile.Width = 140;
    }

    // The stderr console (Terminal.Error) applies the same width itself when stderr is redirected.
}

CommandApp app = new();

app.Configure(config => AppConfiguration.Configure(config, args, GetApplicationVersion()));

return app.Run(args);

static string GetApplicationVersion()
{
    string? informationalVersion = typeof(Program).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    // Strip the source-revision suffix (for example "0.3.0+abc1234") so callers see a clean version.
    return informationalVersion?.Split('+')[0] ?? "0.0.0";
}
