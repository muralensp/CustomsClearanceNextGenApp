using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CustomsClearance.Platform.RulesEngine.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddCustomsClearanceRulesEngine_RegistersAllPipelineServices()
    {
        var services = new ServiceCollection();
        services.AddDbContext<RulesDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddCustomsClearanceRulesEngine();

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IRuleSetLoader>());
        Assert.NotNull(provider.GetRequiredService<IConditionEvaluator>());
        Assert.NotNull(provider.GetRequiredService<IPriorityResolver>());
        Assert.NotNull(provider.GetRequiredService<IDecisionTraceRecorder>());
        Assert.NotNull(provider.GetRequiredService<RulesEngine>());
    }
}
