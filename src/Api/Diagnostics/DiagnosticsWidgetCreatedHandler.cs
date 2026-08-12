using CustomsClearance.Platform.Messaging;
using Microsoft.Extensions.Logging;

namespace CustomsClearance.Api.Diagnostics;

public sealed record DiagnosticsWidgetCreatedPayload(Guid WidgetId, string Name);

/// <summary>
/// Development-only event handler proving Platform.Messaging's pub/sub
/// actually invokes registered handlers, not just that PublishAsync
/// doesn't throw. Logs receipt so the smoke-test verification (Day 18)
/// can confirm it fired by checking the server log.
/// </summary>
public sealed class DiagnosticsWidgetCreatedHandler : IEventHandler<DiagnosticsWidgetCreatedPayload>
{
    private readonly ILogger<DiagnosticsWidgetCreatedHandler> _logger;

    public DiagnosticsWidgetCreatedHandler(ILogger<DiagnosticsWidgetCreatedHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(DomainEventEnvelope<DiagnosticsWidgetCreatedPayload> envelope, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "HANDLED event {EventType} ({EventId}) for widget {WidgetId} ({WidgetName}). CorrelationId={CorrelationId}",
            envelope.EventType,
            envelope.EventId,
            envelope.Payload.WidgetId,
            envelope.Payload.Name,
            envelope.CorrelationId);

        return Task.CompletedTask;
    }
}
