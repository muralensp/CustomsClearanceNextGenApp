using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CustomsClearance.Platform.Logging;

/// <summary>
/// Reads X-Correlation-Id from the incoming request (per API Standards v1.0
/// Section 7); if absent, generates one. Sets CorrelationIdAccessor.Current,
/// echoes the value on the response header (exactly one), and pushes it into the
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
        // Set the response header as the response STARTS (not up front), via the
        // indexer, so it REPLACES rather than adds. Day 41 finding (ADR-0032 side
        // note): behind the Gateway, YARP copies the Api's own X-Correlation-Id
        // response header onto the response, and the Gateway's middleware had
        // already set one - the client received the header twice. Both hops
        // carry the same value (the ID travels downstream as a request header),
        // so overwriting at response start leaves exactly one.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        // Also set on the incoming request headers (not just the response):
        // when this middleware runs in front of a reverse proxy (Day 22
        // Gateway + YARP), the proxy forwards the request's own header
        // collection downstream by default. Without this, a
        // gateway-generated ID (no header on the original client request)
        // never reaches the proxied destination, which then mints its own
        // - producing two different correlation IDs for one request.
        context.Request.Headers[HeaderName] = correlationId;

        using (_logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await _next(context);
        }
    }
}
