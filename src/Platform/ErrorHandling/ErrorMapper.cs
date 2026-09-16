using Microsoft.AspNetCore.Http;

namespace CustomsClearance.Platform.ErrorHandling;

/// <summary>
/// Pure mapping logic (Exception -> ProblemDetailsResponse), deliberately
/// kept separate from ExceptionHandlingMiddleware so it is unit-testable
/// without spinning up an HTTP pipeline (per adr/0009's layer-separation
/// principle: unit tests should not require infrastructure).
/// </summary>
public static class ErrorMapper
{
    private const string _validationFailedType = "https://api.customsclearance.india/errors/validation-failed";
    private const string _malformedRequestType = "https://api.customsclearance.india/errors/malformed-request";
    private const string _unexpectedErrorType = "https://api.customsclearance.india/errors/unexpected";

    public static ProblemDetailsResponse Map(Exception exception, string? instancePath, string? correlationId)
    {
        if (exception is DomainValidationException domainEx)
        {
            var errors = domainEx.Failures
                .Select(f => new ProblemDetailsError(f.Field, f.Code, f.Message))
                .ToArray();

            return new ProblemDetailsResponse(
                Type: _validationFailedType,
                Title: "Business rule validation failed",
                Status: StatusCodes.UnprocessableEntity,
                Detail: domainEx.Message,
                Instance: instancePath,
                CorrelationId: correlationId,
                Errors: errors);
        }

        // A malformed request body (invalid JSON, wrong types, an unknown
        // enum string) - the client's mistake, not the server's - is a 400,
        // not the generic 500 below. Found for real (Day 34 live
        // verification): Minimal API's RequestDelegateFactory throws
        // BadHttpRequestException for exactly this case, and without this
        // branch it fell through to the unexpected-error/500 path, hiding a
        // client-fixable mistake behind a response that looked like a
        // server bug.
        if (exception is BadHttpRequestException badRequestEx)
        {
            return new ProblemDetailsResponse(
                Type: _malformedRequestType,
                Title: "The request could not be understood",
                Status: StatusCodes.BadRequest,
                Detail: badRequestEx.Message,
                Instance: instancePath,
                CorrelationId: correlationId,
                Errors: null);
        }

        // Unknown/unhandled exceptions never leak internal details (stack
        // traces, exception type names, connection strings, etc.) into the
        // response, per Day10_Security_Audit_Standards_v1.0.docx Section 11
        // (Logging Standards - the same "don't leak internals externally"
        // principle applied to the client-facing error response, not just
        // logs). The real exception is logged server-side by the caller
        // (ExceptionHandlingMiddleware), not exposed here.
        return new ProblemDetailsResponse(
            Type: _unexpectedErrorType,
            Title: "An unexpected error occurred",
            Status: StatusCodes.InternalServerError,
            Detail: null,
            Instance: instancePath,
            CorrelationId: correlationId,
            Errors: null);
    }
}

internal static class StatusCodes
{
    public const int BadRequest = 400;
    public const int UnprocessableEntity = 422;
    public const int InternalServerError = 500;
}
