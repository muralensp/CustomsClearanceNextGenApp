using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace CustomsClearance.Platform.Messaging;

/// <summary>
/// Retry-with-backoff around ConnectionFactory.CreateConnection, shared by
/// RabbitMqEventBus and RabbitMqEventConsumer. Found for real (Day 24,
/// same class of bug as Day 18's Postgres finding): docker-compose's
/// rabbitmq healthcheck (`rabbitmq-diagnostics -q ping`, which only checks
/// the Erlang node is up) can report healthy slightly before the AMQP
/// listener (port 5672) actually accepts connections - the Api crashed at
/// startup with BrokerUnreachableException/ConnectionRefused hitting
/// exactly that gap. Synchronous (Thread.Sleep, not Task.Delay) because
/// this runs inside a constructor - both call sites are DI-resolved
/// eagerly at host startup, where an async constructor isn't available;
/// this mirrors the async retry loops already used for DiagnosticsDb/
/// IdentityDb in Program.cs, just adapted to a sync context.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Thin wrapper around real RabbitMQ network I/O - see RabbitMqEventBus's coverage-exclusion justification.")]
internal static class RabbitMqConnectionHelper
{
    public static IConnection CreateConnectionWithRetry(IConnectionFactory factory, string clientProvidedName, ILogger logger, int maxAttempts = 10, int delayMilliseconds = 2000)
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return factory.CreateConnection(clientProvidedName);
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                logger.LogWarning(ex, "RabbitMQ not ready (attempt {Attempt}/{MaxAttempts}), retrying in {DelayMs}ms...", attempt, maxAttempts, delayMilliseconds);
                Thread.Sleep(delayMilliseconds);
            }
        }

        // Last attempt: let a real failure surface (not caught above, since
        // attempt == maxAttempts there), same "give up loudly" behavior as
        // Program.cs's DiagnosticsDb/IdentityDb retry loops.
        return factory.CreateConnection(clientProvidedName);
    }
}
