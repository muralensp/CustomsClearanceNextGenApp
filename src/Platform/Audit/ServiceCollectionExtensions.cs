using Microsoft.Extensions.DependencyInjection;

namespace CustomsClearance.Platform.Audit;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers ConsoleAuditWriter as the default IAuditWriter. Call
    /// again with a different registration (e.g. a future
    /// PostgresAuditWriter in an Infrastructure project) to override once
    /// the real sink exists - this is intentionally the seam for that.
    /// </summary>
    public static IServiceCollection AddCustomsClearanceAudit(this IServiceCollection services)
    {
        services.AddSingleton<IAuditWriter, ConsoleAuditWriter>();
        return services;
    }
}
