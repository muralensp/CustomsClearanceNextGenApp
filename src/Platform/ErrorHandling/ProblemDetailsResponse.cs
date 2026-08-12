namespace CustomsClearance.Platform.ErrorHandling;

/// <summary>
/// A single entry in ProblemDetailsResponse.Errors, per API Standards v1.0
/// Section 6.
/// </summary>
public sealed record ProblemDetailsError(string? Field, string Code, string Message);

/// <summary>
/// The platform-wide error response envelope, per API Standards v1.0
/// Section 6 (RFC 7807 Problem Details, extended with correlationId and a
/// PRD-Business-Rule-ID-coded errors array). Every context-owned API
/// returns this exact shape for any error response - built once here so
/// no individual context has to reinvent it.
/// </summary>
public sealed record ProblemDetailsResponse(
    string Type,
    string Title,
    int Status,
    string? Detail,
    string? Instance,
    string? CorrelationId,
    IReadOnlyList<ProblemDetailsError>? Errors);
