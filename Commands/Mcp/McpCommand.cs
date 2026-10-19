using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Spectre.Console.Cli;

namespace Catalyst.Commands.Mcp;

/// <summary>Registers the Catalyst tools; shared by the real server and the in-process tests.</summary>
internal static class McpRegistration
{
    public static IMcpServerBuilder AddCatalystServer(this IServiceCollection services)
    {
        return services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new ModelContextProtocol.Protocol.Implementation { Name = "catalyst", Version = Version() };
                options.ServerInstructions = "Catalyst looks up and generates TypeScript API clients from OpenAPI specs. "
                    + "Start with list_clients, then list_operations and show_operation to learn how to call an endpoint; "
                    + "use diff_spec to see what a spec change breaks and generate_clients to regenerate.";
            })
            .WithTools([typeof(CatalystTools)]);
    }

    private static string Version()
    {
        string? informational = typeof(McpRegistration).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return informational?.Split('+')[0] ?? "0.0.0";
    }
}

[Description("Runs a Model Context Protocol server over stdio, so AI agents can call Catalyst's lookup, diff and generate tools.")]
internal sealed class McpCommand : AsyncCommand<McpCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
    }

    protected override async Task<int> ExecuteAsync([NotNull] CommandContext context, [NotNull] Settings settings, CancellationToken cancellationToken)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();

        // stdout is the protocol channel, so logging goes to stderr only.
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

        builder.Services.AddCatalystServer().WithStdioServerTransport();

        await builder.Build().RunAsync(cancellationToken).ConfigureAwait(false);
        return 0;
    }
}
