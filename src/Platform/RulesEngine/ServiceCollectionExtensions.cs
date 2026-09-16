using Microsoft.Extensions.DependencyInjection;

namespace CustomsClearance.Platform.RulesEngine;

/// <summary>
/// Registers the evaluation pipeline behind RulesDbContext, same
/// AddCustomsClearance* convention as every other cross-cutting library.
/// IDecisionTraceRecorder now resolves to EfDecisionTraceRecorder (Day 33)
/// - real, persisted trace capture - replacing Day 32's in-memory
/// placeholder without any change to RulesEngine's constructor or the
/// interface itself, exactly as ADR-0013/ADR-0026 anticipated.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCustomsClearanceRulesEngine(this IServiceCollection services)
    {
        services.AddScoped<IRuleSetLoader, EfRuleSetLoader>();
        services.AddSingleton<IConditionEvaluator, ConditionEvaluator>();
        services.AddSingleton<IPriorityResolver, PriorityResolver>();
        services.AddScoped<IDecisionTraceRecorder, EfDecisionTraceRecorder>();
        services.AddScoped<RulesEngine>();

        return services;
    }
}
