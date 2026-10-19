namespace CustomsClearance.Gateway;

/// <summary>
/// Gateway rate-limit settings, bound from the "RateLimiting" configuration
/// section (Day 42, ADR-0037). The defaults are the original Day 22 values
/// (100 requests per 10 seconds per client IP), so with no configuration the
/// behaviour is unchanged. They are configurable so a load test from one
/// machine is not simply answered with HTTP 429, and so the real limit can
/// be tuned per environment later instead of by a code change.
/// </summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public int PermitLimit { get; set; } = 100;

    public int WindowSeconds { get; set; } = 10;

    /// <summary>
    /// Fails fast at start-up on a non-positive value: a zero or negative
    /// limit would silently reject all traffic or throw on the first request.
    /// </summary>
    public void Validate()
    {
        if (PermitLimit <= 0)
        {
            throw new InvalidOperationException(
                $"{SectionName}:PermitLimit must be greater than zero (got {PermitLimit}).");
        }

        if (WindowSeconds <= 0)
        {
            throw new InvalidOperationException(
                $"{SectionName}:WindowSeconds must be greater than zero (got {WindowSeconds}).");
        }
    }
}
