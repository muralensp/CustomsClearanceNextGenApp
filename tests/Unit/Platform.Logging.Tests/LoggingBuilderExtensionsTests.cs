using CustomsClearance.Platform.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Xunit;

namespace CustomsClearance.Platform.Logging.Tests;

/// <summary>
/// Proves AddCustomsClearanceStructuredLogging (Day 26) actually wires the
/// JSON console formatter, not just that it doesn't throw - resolves the
/// real ConsoleFormatterOptions the ConsoleLoggerProvider will use and
/// checks its name matches ConsoleFormatterNames.Json.
/// </summary>
public class LoggingBuilderExtensionsTests
{
    [Fact]
    public void AddCustomsClearanceStructuredLogging_ConfiguresJsonConsoleFormatter()
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddCustomsClearanceStructuredLogging());
        using var provider = services.BuildServiceProvider();

        var consoleOptions = provider.GetRequiredService<IOptionsMonitor<ConsoleLoggerOptions>>().CurrentValue;

        Assert.Equal(ConsoleFormatterNames.Json, consoleOptions.FormatterName);
    }

    [Fact]
    public void AddCustomsClearanceStructuredLogging_LoggerActuallyLogsWithoutThrowing()
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddCustomsClearanceStructuredLogging());
        using var provider = services.BuildServiceProvider();

        var logger = provider.GetRequiredService<ILogger<LoggingBuilderExtensionsTests>>();

        var exception = Record.Exception(() => logger.LogInformation("Day 26 structured logging smoke test."));

        Assert.Null(exception);
    }
}
