using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace CustomsClearance.Platform.Messaging;

/// <summary>
/// Real, cross-process IEventPublisher (Day 24) - the swap ADR-0012 named
/// as a future step behind AddCustomsClearanceMessaging(), without
/// changing the envelope/handler contract application code depends on.
/// Publishes the whole DomainEventEnvelope&lt;TPayload&gt; as JSON to a
/// topic exchange, routed by EventType, so a future second consumer
/// (different queue, same exchange) could subscribe to a subset of event
/// types without this publisher changing.
///
/// One shared IConnection (thread-safe per the RabbitMQ.Client docs) and,
/// since Day 45 (adr/0040-rabbitmq-publish-channel-reuse.md),
/// ONE long-lived publishing IModel guarded by a lock, recreated if the
/// broker closes it. IModel is not safe for concurrent publishes, so
/// publishes are serialised; BasicPublish without confirms is a local
/// buffer write, so the lock is held for microseconds. Before Day 45 this
/// opened a channel, declared the exchange and closed the channel on EVERY
/// publish (three synchronous broker round trips inside the request) and a
/// Day 42/45 trace measurement showed that publish step taking ~4.6 ms of a
/// 10 ms request unloaded and ~2.6 s of the slowest 2.7 s requests under
/// load.
///
/// Excluded from the ADR-0007 coverage gate: this class's real work is a
/// live network connection to RabbitMQ, which this codebase has no
/// mocking library to fake (no Moq/NSubstitute anywhere, by design - see
/// EfRepository's real-Postgres-only proof pattern) and hand-writing fakes
/// for IConnection/IModel's full surface isn't proportionate to what Day
/// 24 needs. Proven instead by a real docker-compose run (see
/// adr/0018/Day24_Execution_Log.md) - the same "container smoke-test
/// layer, not unit tests" split ADR-0009 already established for
/// Postgres. The one piece of real risk here (reflection-based dispatch)
/// is NOT excluded - see DomainEventDispatcher, which is fully unit-tested.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Real RabbitMQ network I/O - no mocking library in this codebase (ADR-0007/0009 precedent); proven via live docker-compose run instead. See class doc comment.")]
public sealed class RabbitMqEventBus : IEventPublisher, IDisposable
{
    private readonly IConnection _connection;
    private readonly object _publishLock = new();
    private IModel? _publishChannel;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqEventBus> _logger;

    public RabbitMqEventBus(IOptions<RabbitMqOptions> options, ILogger<RabbitMqEventBus> logger)
    {
        _options = options.Value;
        _logger = logger;

        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
        };
        _connection = RabbitMqConnectionHelper.CreateConnectionWithRetry(factory, "CustomsClearance.Api", logger);
    }

    public Task PublishAsync<TPayload>(DomainEventEnvelope<TPayload> envelope, CancellationToken cancellationToken = default)
    {
        // Day 30 finding: carry the current trace context across the AMQP
        // hop via a "traceparent" message header (W3C format, same as the
        // header HTTP instrumentation uses) - RabbitMqEventConsumer reads
        // it back to make the consumer span a real child of this publish
        // span, not a disconnected trace.
        using var activity = MessagingTelemetry.ActivitySource.StartActivity(
            $"{envelope.EventType} publish", ActivityKind.Producer);
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination", _options.ExchangeName);
        activity?.SetTag("correlation_id", envelope.CorrelationId);

        var body = JsonSerializer.SerializeToUtf8Bytes(envelope);

        lock (_publishLock)
        {
            try
            {
                var channel = GetPublishChannel();
                var properties = channel.CreateBasicProperties();
                properties.ContentType = "application/json";
                properties.MessageId = envelope.EventId.ToString();
                properties.Persistent = true;
                if (activity?.Id is { } traceparent)
                {
                    properties.Headers = new Dictionary<string, object> { ["traceparent"] = traceparent };
                }

                channel.BasicPublish(
                    exchange: _options.ExchangeName,
                    routingKey: envelope.EventType,
                    basicProperties: properties,
                    body: body);
            }
            catch
            {
                // A channel that threw is in an unknown state: drop it so the next publish opens a fresh one.
                DiscardPublishChannel();
                throw;
            }
        }

        _logger.LogInformation(
            "Published event {EventType} v{EventVersion} ({EventId}) from {Source} to RabbitMQ exchange {Exchange}. CorrelationId={CorrelationId}",
            envelope.EventType,
            envelope.EventVersion,
            envelope.EventId,
            envelope.Source,
            _options.ExchangeName,
            envelope.CorrelationId);

        return Task.CompletedTask;
    }

    // Callers hold _publishLock.
    private IModel GetPublishChannel()
    {
        if (_publishChannel is { IsOpen: true } open)
        {
            return open;
        }

        DiscardPublishChannel();
        var channel = _connection.CreateModel();
        channel.ExchangeDeclare(_options.ExchangeName, ExchangeType.Topic, durable: true);
        _publishChannel = channel;
        return channel;
    }

    // Callers hold _publishLock (or are disposing).
    private void DiscardPublishChannel()
    {
        var channel = _publishChannel;
        _publishChannel = null;
        try
        {
            channel?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Ignoring error while discarding the RabbitMQ publish channel");
        }
    }

    public void Dispose()
    {
        lock (_publishLock)
        {
            DiscardPublishChannel();
        }

        _connection.Close();
        _connection.Dispose();
    }
}
