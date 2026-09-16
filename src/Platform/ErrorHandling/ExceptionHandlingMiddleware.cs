using System.Text.Json;
using CustomsClearance.Platform.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CustomsClearance.Platform.ErrorHandling;

/// <summary>
/// Catches any exception that escapes the request pipeline and translates
/// it into the platform-wide ProblemDetailsResponse shape (API Standards
/// v1.0 Section 6). Must be registered after
/// UseCustomsClearanceCorrelationId (Platform.Logging) so CorrelationId is
/// already set when an error is mapped.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            var correlationId = CorrelationIdAccessor.Current;
            var response = ErrorMapper.Map(ex, context.Request.Path, correlationId);

            if (ex is DomainValidationException)
            {
                // Expected, business-rule-driven failure - Warning, not Error.
                _logger.LogWarning(ex, "Business rule validation failed. CorrelationId={CorrelationId}", correlationId);
            }
            else if (ex is Microsoft.AspNetCore.Http.BadHttpRequestException)
            {
                // A malformed request body is a client mistake, not a
                // server fault - Warning, not Error, same as
                // DomainValidationException above.
                _logger.LogWarning(ex, "Malformed request. CorrelationId={CorrelationId}", correlationId);
            }
            else
            {
                // Unexpected failure - the full exception (including stack
                // trace) is logged server-side only; the client response
                // (ErrorMapper.Map above) deliberately omits these details.
                _logger.LogError(ex, "Unhandled exception. CorrelationId={CorrelationId}", correlationId);
            }

            context.Response.ContentType = "application/problem+json";
            context.Response.StatusCode = response.Status;
            await context.Response.WriteAsync(JsonSerializer.Serialize(response, _serializerOptions));
        }
    }
}
