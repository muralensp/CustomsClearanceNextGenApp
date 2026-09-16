using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace CustomsClearance.Platform.Logging;

public static class LoggingBuilderExtensions
{
    /// <summary>
    /// Day 26 - replaces the default plain-text console provider with
    /// .NET's built-in JSON console formatter, so every log line (already
    /// carrying the CorrelationId scope CorrelationIdMiddleware sets) is
    /// structured and machine-parseable. No new dependency - AddJsonConsole
    /// ships in Microsoft.Extensions.Logging.Console, already available via
    /// the ASP.NET Core shared framework both the Api host and Gateway
    /// reference. This is what makes "centralize log output" (Day 26's
    /// deliverable) meaningful: Promtail reads each container's JSON stdout
    /// directly, no separate log-shipping agent or format translation
    /// needed. ClearProviders() removes the default Console/Debug/
    /// EventSource providers WebApplication.CreateBuilder wires in, but not
    /// the LogLevel filtering already bound from the "Logging" config
    /// section (that's separate configuration binding, unaffected by
    /// provider changes).
    /// </summary>
    public static ILoggingBuilder AddCustomsClearanceStructuredLogging(this ILoggingBuilder logging)
    {
        logging.ClearProviders();
        logging.AddConsole(options => options.FormatterName = ConsoleFormatterNames.Json);
        return logging;
    }
}
