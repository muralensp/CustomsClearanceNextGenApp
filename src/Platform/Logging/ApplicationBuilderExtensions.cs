using Microsoft.AspNetCore.Builder;

namespace CustomsClearance.Platform.Logging;

public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Registers the correlation ID middleware. Must be added early in the
    /// pipeline - before any logging or error handling that should carry
    /// the correlation ID (see ApplicationBuilderExtensions in
    /// Platform.ErrorHandling, which depends on this being registered first).
    /// </summary>
    public static IApplicationBuilder UseCustomsClearanceCorrelationId(this IApplicationBuilder app)
    {
        return app.UseMiddleware<CorrelationIdMiddleware>();
    }
}
