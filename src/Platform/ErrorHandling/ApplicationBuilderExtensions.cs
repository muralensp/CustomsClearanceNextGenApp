using Microsoft.AspNetCore.Builder;

namespace CustomsClearance.Platform.ErrorHandling;

public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Registers the platform-wide exception handling middleware. Register
    /// this AFTER app.UseCustomsClearanceCorrelationId() (Platform.Logging)
    /// so a mapped error response always carries the correct CorrelationId.
    /// </summary>
    public static IApplicationBuilder UseCustomsClearanceErrorHandling(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ExceptionHandlingMiddleware>();
    }
}
