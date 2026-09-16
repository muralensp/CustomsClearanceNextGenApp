using Microsoft.Extensions.DependencyInjection;

namespace CustomsClearance.Platform.RulesEngine;

/// <summary>
/// Registers the Day 32 evaluation pipeline behind RulesDbContext, same
/// AddCustomsClearance* convention as every other cross-cutting library.
/// IDecisionTraceRecorder registers the Day 32 in-memory implementation -
/// Day 33 replaces this binding with a real EF-backed one without
/// changing RulesEngine's constructor or any calling code.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCustomsClearanceRulesEngine(this IServiceCollection services)
    {
        services.AddScoped<IRuleSetLoader, EfRuleSetLoader>();
        services.AddSingleton<IConditionEvaluator, ConditionEvaluator>();
        services.AddSingleton<IPriorityResolver, PriorityResolver>();
        services.AddScoped<IDecisionTraceRecorder, InMemoryDecisionTraceRecorder>();
        services.AddScoped<RulesEngine>();

        return services;
    }
}
