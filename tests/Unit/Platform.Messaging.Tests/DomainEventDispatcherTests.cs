using System.Text.Json;
using CustomsClearance.Platform.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CustomsClearance.Platform.Messaging.Tests;

/// <summary>
/// Unit-tests the reflection-based dispatch RabbitMqEventConsumer relies
/// on (Day 24), without needing a real RabbitMQ broker - this is the
/// highest-risk part of the real event bus (generic type resolution +
/// reflection invocation), so it's covered directly rather than only via
/// the live docker-compose proof.
/// </summary>
public class DomainEventDispatcherTests
{
    private static DomainEventDispatcher CreateDispatcher(EventTypeRegistry registry, IServiceCollection services)
    {
        services.AddLogging();
        var provider = services.BuildServiceProvider(validateScopes: true);
        return new DomainEventDispatcher(
            registry,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<DomainEventDispatcher>.Instance);
    }

    [Fact]
    public async Task DispatchAsync_RegisteredEventType_InvokesHandlerWithDeserializedEnvelope()
    {
        var registry = new EventTypeRegistry();
        registry.Register<TestPayload>("TestEvent");

        var handler = new RecordingHandler();
        var services = new ServiceCollection();
        services.AddScoped<IEventHandler<TestPayload>>(_ => handler);
        var dispatcher = CreateDispatcher(registry, services);

        var envelope = DomainEventEnvelope<TestPayload>.Create("TestEvent", "1.0", "TestSource", new TestPayload("hello-from-wire"));
        var json = JsonSerializer.Serialize(envelope);

        await dispatcher.DispatchAsync("TestEvent", json);

        Assert.Single(handler.Received);
        Assert.Equal(envelope.EventId, handler.Received[0].EventId);
        Assert.Equal("hello-from-wire", handler.Received[0].Payload.Message);
    }

    [Fact]
    public async Task DispatchAsync_MultipleHandlersForSamePayloadType_InvokesAll()
    {
        var registry = new EventTypeRegistry();
        registry.Register<TestPayload>("TestEvent");

        var handlerA = new RecordingHandler();
        var handlerB = new RecordingHandler();
        var services = new ServiceCollection();
        services.AddScoped<IEventHandler<TestPayload>>(_ => handlerA);
        services.AddScoped<IEventHandler<TestPayload>>(_ => handlerB);
        var dispatcher = CreateDispatcher(registry, services);

        var envelope = DomainEventEnvelope<TestPayload>.Create("TestEvent", "1.0", "TestSource", new TestPayload("fan-out"));
        var json = JsonSerializer.Serialize(envelope);

        await dispatcher.DispatchAsync("TestEvent", json);

        Assert.Single(handlerA.Received);
        Assert.Single(handlerB.Received);
    }

    [Fact]
    public async Task DispatchAsync_UnregisteredEventType_DoesNotThrowAndSkips()
    {
        var registry = new EventTypeRegistry();
        var handler = new RecordingHandler();
        var services = new ServiceCollection();
        services.AddScoped<IEventHandler<TestPayload>>(_ => handler);
        var dispatcher = CreateDispatcher(registry, services);

        await dispatcher.DispatchAsync("SomeUnknownEvent", "{}");

        Assert.Empty(handler.Received);
    }

    [Fact]
    public async Task DispatchAsync_NoRegisteredHandlers_DoesNotThrow()
    {
        var registry = new EventTypeRegistry();
        registry.Register<TestPayload>("TestEvent");
        var services = new ServiceCollection();
        var dispatcher = CreateDispatcher(registry, services);

        var envelope = DomainEventEnvelope<TestPayload>.Create("TestEvent", "1.0", "TestSource", new TestPayload("no-handlers"));
        var json = JsonSerializer.Serialize(envelope);

        var exception = await Record.ExceptionAsync(() => dispatcher.DispatchAsync("TestEvent", json));

        Assert.Null(exception);
    }
}
