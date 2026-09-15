using CustomsClearance.Platform.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace CustomsClearance.Platform.Messaging.Tests;

/// <summary>
/// Covers AddCustomsClearanceRabbitMqMessaging's registration/options-
/// binding logic without resolving IEventPublisher/RabbitMqEventConsumer
/// themselves - doing so would open a real RabbitMQ TCP connection (see
/// RabbitMqEventBus's constructor), which this unit-test layer must not
/// require (ADR-0009: no infrastructure dependency).
/// </summary>
public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddCustomsClearanceRabbitMqMessaging_BindsOptionsAndRegistersServices()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RabbitMq:HostName"] = "test-host",
                ["RabbitMq:Port"] = "5673",
                ["RabbitMq:ExchangeName"] = "test-exchange",
                ["RabbitMq:QueueName"] = "test-queue",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCustomsClearanceRabbitMqMessaging(config, registry =>
        {
            registry.Register<TestPayload>("TestEvent");
        });

        Assert.Contains(services, d => d.ServiceType == typeof(IEventPublisher) && d.ImplementationType == typeof(RabbitMqEventBus));
        Assert.Contains(services, d => d.ServiceType == typeof(DomainEventDispatcher));
        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(RabbitMqEventConsumer));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<RabbitMqOptions>>().Value;
        Assert.Equal("test-host", options.HostName);
        Assert.Equal(5673, options.Port);
        Assert.Equal("test-exchange", options.ExchangeName);
        Assert.Equal("test-queue", options.QueueName);

        var registry = provider.GetRequiredService<EventTypeRegistry>();
        Assert.Equal(typeof(TestPayload), registry.ResolvePayloadType("TestEvent"));
    }
}
