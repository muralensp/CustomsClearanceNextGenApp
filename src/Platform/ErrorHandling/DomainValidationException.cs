namespace CustomsClearance.Platform.ErrorHandling;

/// <summary>
/// A single field-level validation failure within a DomainValidationException,
/// per API Standards v1.0 Section 6 (errors[].code maps to a PRD Business
/// Rule ID, e.g. "BR-SHP-003").
/// </summary>
/// <param name="Field">The field that failed validation, or null for a
/// non-field-specific rule violation.</param>
/// <param name="Code">The PRD Business Rule ID this violation maps to
/// (e.g. "BR-DEC-004"), per Day 8 Coding Standards Section 4.</param>
/// <param name="Message">Human-readable description of the failure.</param>
public sealed record ValidationFailure(string? Field, string Code, string Message);

/// <summary>
/// The single exception type business logic throws for any business rule
/// violation, per Day8_Coding_Standards_v1.0.docx Section 4: "All business
/// rule violations throw a single DomainValidationException carrying a
/// BusinessRuleCode ... this is what the API layer catches and translates
/// into the RFC 7807 error format." One or more ValidationFailures may be
/// carried (a single failure is the common case; multiple supports
/// batch-validation scenarios like FR-DEC-002's declaration validation).
/// </summary>
public sealed class DomainValidationException : Exception
{
    public IReadOnlyList<ValidationFailure> Failures { get; }

    public DomainValidationException(string code, string message, string? field = null)
        : base(message)
    {
        Failures = new[] { new ValidationFailure(field, code, message) };
    }

    public DomainValidationException(IReadOnlyList<ValidationFailure> failures)
        : base(failures.Count == 1 ? failures[0].Message : $"{failures.Count} validation failures occurred.")
    {
        if (failures.Count == 0)
        {
            throw new ArgumentException("At least one validation failure is required.", nameof(failures));
        }

        Failures = failures;
    }
}
