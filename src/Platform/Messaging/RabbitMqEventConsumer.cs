using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace CustomsClearance.Platform.Messaging;

/// <summary>
/// The consuming half of Day 24's real event bus: binds this host's queue
/// to the topic exchange RabbitMqEventBus publishes to, and for each
/// message delegates to DomainEventDispatcher (type resolution,
/// deserialization, and IEventHandler&lt;TPayload&gt; invocation - kept
/// separate so that logic is unit-testable without a real broker; this
/// class is intentionally just RabbitMQ connection/channel plumbing).
///
/// Manual ack only after successful handling - an unhandled exception
/// nacks with requeue, giving RabbitMQ's own redelivery a chance rather
/// than silently losing the event. No dead-letter queue yet (Day 24
/// scope: prove one sample flow, not full delivery-failure semantics) -
/// flagged for revisit once real event volume exists.
///
/// Excluded from the ADR-0007 coverage gate for the same reason as
/// RabbitMqEventBus - real network I/O, no mocking library in this
/// codebase, proven via live docker-compose run instead. The dispatch
/// logic this delegates to (DomainEventDispatcher) is NOT excluded and is
/// fully unit-tested.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Real RabbitMQ network I/O - no mocking library in this codebase (ADR-0007/0009 precedent); proven via live docker-compose run instead. See class doc comment.")]
public sealed class RabbitMqEventConsumer : BackgroundService
{
    private readonly IConnection _connection;
    private readonly RabbitMqOptions _options;
    private readonly DomainEventDispatcher _dispatcher;
    private readonly ILogger<RabbitMqEventConsumer> _logger;
    private readonly IModel _channel;

    public RabbitMqEventConsumer(
        IOptions<RabbitMqOptions> options,
        DomainEventDispatcher dispatcher,
        ILogger<RabbitMqEventConsumer> logger)
    {
        _options = options.Value;
        _dispatcher = dispatcher;
        _logger = logger;

        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            DispatchConsumersAsync = true,
        };
        _connection = RabbitMqConnectionHelper.CreateConnectionWithRetry(factory, "CustomsClearance.Api.Consumer", logger);
        _channel = _connection.CreateModel();
        _channel.ExchangeDeclare(_options.ExchangeName, ExchangeType.Topic, durable: true);
        _channel.QueueDeclare(_options.QueueName, durable: true, exclusive: false, autoDelete: false);
        _channel.QueueBind(_options.QueueName, _options.ExchangeName, routingKey: "#");
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += async (_, args) =>
        {
            try
            {
                // Day 30 finding: link this consumer span to the publisher's
                // span via the "traceparent" header RabbitMqEventBus sets,
                // so the trace shows publish -> consume as one connected
                // chain instead of two disconnected traces.
                string? traceparent = null;
                if (args.BasicProperties?.Headers is { } headers &&
                    headers.TryGetValue("traceparent", out var raw) && raw is byte[] bytes)
                {
                    traceparent = Encoding.UTF8.GetString(bytes);
                }

                using var activity = MessagingTelemetry.ActivitySource.StartActivity(
                    $"{args.RoutingKey} receive", ActivityKind.Consumer, traceparent ?? string.Empty);
                activity?.SetTag("messaging.system", "rabbitmq");
                activity?.SetTag("messaging.destination", _options.ExchangeName);

                var json = Encoding.UTF8.GetString(args.Body.Span);
                await _dispatcher.DispatchAsync(args.RoutingKey, json, stoppingToken);
                _channel.BasicAck(args.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to handle RabbitMQ message {RoutingKey}, requeueing.", args.RoutingKey);
                _channel.BasicNack(args.DeliveryTag, multiple: false, requeue: true);
            }
        };

        _channel.BasicConsume(_options.QueueName, autoAck: false, consumer);
        return Task.CompletedTask;
    }

    public override void Dispose()
    {
        _channel.Close();
        _channel.Dispose();
        _connection.Close();
        _connection.Dispose();
        base.Dispose();
    }
}
