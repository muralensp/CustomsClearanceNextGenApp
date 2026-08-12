using CustomsClearance.Platform.Logging;
using CustomsClearance.Platform.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CustomsClearance.Platform.Messaging.Tests;

public sealed record TestPayload(string Message);

public sealed class RecordingHandler : IEventHandler<TestPayload>
{
    public List<DomainEventEnvelope<TestPayload>> Received { get; } = new();

    public Task HandleAsync(DomainEventEnvelope<TestPayload> envelope, CancellationToken cancellationToken = default)
    {
        Received.Add(envelope);
        return Task.CompletedTask;
    }
}

public class InMemoryEventBusTests
{
    [Fact]
    public void DomainEventEnvelope_Create_StampsIdTimestampAndCorrelationId()
    {
        CorrelationIdAccessor.Current = "bus-test-corr-1";
        var before = DateTimeOffset.UtcNow;

        var envelope = DomainEventEnvelope<TestPayload>.Create(
            eventType: "TestEvent",
            eventVersion: "1.0",
            source: "TestSource",
            payload: new TestPayload("hello"));

        var after = DateTimeOffset.UtcNow;

        Assert.NotEqual(Guid.Empty, envelope.EventId);
        Assert.InRange(envelope.OccurredAt, before, after);
        Assert.Equal("bus-test-corr-1", envelope.CorrelationId);
        Assert.Equal("hello", envelope.Payload.Message);
    }

    [Fact]
    public async Task PublishAsync_InvokesAllRegisteredHandlersForPayloadType()
    {
        // Handlers registered Scoped deliberately (not Singleton) - this is
        // what a real handler with a DbContext dependency would look like,
        // and it's exactly the registration lifetime that exposed the
        // "Cannot resolve scoped service from root provider" bug found
        // against the real Api host on Day 18 (fixed by using
        // IServiceScopeFactory instead of IServiceProvider in
        // InMemoryEventBus). ValidateScopes:true makes BuildServiceProvider
        // itself throw on this class of bug, so this test would have
        // caught it before the manual curl verification did.
        var handlerA = new RecordingHandler();
        var handlerB = new RecordingHandler();

        var services = new ServiceCollection();
        services.AddScoped<IEventHandler<TestPayload>>(_ => handlerA);
        services.AddScoped<IEventHandler<TestPayload>>(_ => handlerB);
        services.AddLogging();
        var provider = services.BuildServiceProvider(validateScopes: true);

        var bus = new InMemoryEventBus(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<InMemoryEventBus>>());
        var envelope = DomainEventEnvelope<TestPayload>.Create("TestEvent", "1.0", "TestSource", new TestPayload("hi"));

        await bus.PublishAsync(envelope);

        Assert.Single(handlerA.Received);
        Assert.Single(handlerB.Received);
        Assert.Equal(envelope.EventId, handlerA.Received[0].EventId);
    }

    [Fact]
    public async Task PublishAsync_WithNoRegisteredHandlers_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var provider = services.BuildServiceProvider(validateScopes: true);
        var bus = new InMemoryEventBus(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<InMemoryEventBus>>());
        var envelope = DomainEventEnvelope<TestPayload>.Create("TestEvent", "1.0", "TestSource", new TestPayload("no handlers"));

        var task = bus.PublishAsync(envelope);
        await task;

        Assert.True(task.IsCompletedSuccessfully);
    }
}
