using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CustomsClearance.Platform.Logging;

/// <summary>
/// Reads X-Correlation-Id from the incoming request (per API Standards v1.0
/// Section 7); if absent, generates one. Sets CorrelationIdAccessor.Current,
/// echoes the value on the response header, and pushes it into the
/// structured logging scope so every log line emitted while handling this
/// request carries the correlation ID automatically (Day 16 - "structured
/// logs, correlation ID propagation").
/// </summary>
public class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var headerValue) && !string.IsNullOrWhiteSpace(headerValue)
            ? headerValue.ToString()
            : Guid.NewGuid().ToString();

        CorrelationIdAccessor.Current = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (_logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await _next(context);
        }
    }
}
