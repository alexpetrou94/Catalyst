using Catalyst.Common.Models;
using Catalyst.Common.Services;
using Spectre.Console.Cli;

namespace Catalyst.Commands.Gen;

/// <summary>
/// How the CLI treats usage errors: unknown options and stray arguments are rejected (strict parsing) and
/// every usage error, whoever finds it, exits with <see cref="RunReport.UsageExitCode"/>.
/// </summary>
internal static class UsageErrors
{
    /// <summary>Applies strict parsing and the usage error handler to the app configuration.</summary>
    public static void Configure(IConfigurator config, string[] args)
    {
        config.UseStrictParsing();
        config.SetExceptionHandler((exception, _) => Handle(exception, args));
    }

    /// <summary>
    /// Spectre reports parse errors (unknown option, missing value, stray argument) and our own Validate()
    /// failures as exceptions. Both become the same usage exit code, as text on stderr or, when --json was
    /// given, as the USAGE report on stdout. Anything else is an unexpected crash.
    /// </summary>
    internal static int Handle(Exception exception, string[] args)
    {
        if (exception is CommandParseException or CommandRuntimeException)
        {
            if (args.Contains("--json"))
            {
                RunReport report = RunReport.Failure(GuessMode(args), IssueCodes.Usage, exception.Message);
                ReportRenderer.Render(report, json: true);
                return report.ExitCode;
            }

            Terminal.WriteError(exception.Message);
            return RunReport.UsageExitCode;
        }

        Terminal.WriteError(exception.ToString());
        return 1;
    }

    /// <summary>The mode the arguments were heading for; only used to label a report the parser could not finish.</summary>
    private static string GuessMode(string[] args)
    {
        if (args.Contains("--check"))
        {
            return RunMode.Check;
        }

        bool generates = !args.Contains("--source") || args.Contains("--output") || args.Contains("--language");
        return generates ? RunMode.Generate : RunMode.Validate;
    }
}
