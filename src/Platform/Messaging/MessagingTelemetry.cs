using System.Diagnostics;

namespace CustomsClearance.Platform.Messaging;

/// <summary>
/// Day 30 (Observability Review) finding: Day 28's distributed tracing only
/// instrumented the HTTP hop (Gateway -&gt; Api), not the RabbitMQ event bus
/// (Day 24) - so a request that triggered a domain event dropped out of the
/// trace at the point of publish, even though the Day-Wise Plan's Day 28
/// scope explicitly named "gateway -&gt; service -&gt; event bus." Fixed here:
/// a shared ActivitySource RabbitMqEventBus/RabbitMqEventConsumer use to
/// produce a Producer span (on publish) and a Consumer span (on receive),
/// linked via the same W3C traceparent propagation OpenTelemetry's own
/// exporters understand - carried across the wire as a RabbitMQ message
/// header, since AMQP has no built-in trace-context carrier the way HTTP
/// headers do.
///
/// Uses .NET's built-in System.Diagnostics.Activity (W3C ID format by
/// default) rather than taking a new OpenTelemetry.Api package dependency
/// in Platform.Messaging - AddSource("CustomsClearance.Platform.Messaging")
/// in Platform.Observability.AddCustomsClearanceTracing is what makes the
/// OpenTelemetry SDK actually listen to and export these spans.
/// </summary>
public static class MessagingTelemetry
{
    public const string ActivitySourceName = "CustomsClearance.Platform.Messaging";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}
