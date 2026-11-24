using Microsoft.AspNetCore.Builder;
using Serilog;

namespace Adventures.WebApi;

/// <summary>The one logging recipe every host uses (moved from the API host, R1 of the host reuse review): a text log for people and a compact JSON sibling for tools, every line carrying the request's CorrelationId.</summary>
public static class HostLogging
{
    private const string LogTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}";

    /// <summary>Configures Serilog from configuration and services, with console, a rolling text file (<c>logs/{prefix}-.log</c>) and a rolling compact JSON file (<c>logs/{prefix}-json-.log</c>), 14 days kept.</summary>
    public static void UseAdventuresSerilog(this ConfigureHostBuilder host, string logFilePrefix = "webapi")
    {
        host.UseSerilog((context, services, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate: LogTemplate)
            .WriteTo.File(
                Path.Combine(AppContext.BaseDirectory, "logs", logFilePrefix + "-.log"),
                outputTemplate: LogTemplate,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .WriteTo.File(
                new Serilog.Formatting.Compact.RenderedCompactJsonFormatter(),
                Path.Combine(AppContext.BaseDirectory, "logs", logFilePrefix + "-json-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14));
    }
}
