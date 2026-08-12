namespace CustomsClearance.Platform.Logging;

/// <summary>
/// Ambient accessor for the current request's correlation ID, per
/// Day6_API_Standards_v1.0.docx (Section 7) / v1.1 (Section 3.1) - one
/// CorrelationId threads through the originating REST request, every
/// internal service-to-service call, every published domain/integration
/// event, and every audit record. AsyncLocal so the value flows correctly
/// through async/await continuations without needing to be passed
/// explicitly through every method signature.
/// </summary>
public static class CorrelationIdAccessor
{
    private static readonly AsyncLocal<string?> _current = new();

    public static string? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}
