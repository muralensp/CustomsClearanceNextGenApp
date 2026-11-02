namespace CustomsClearance.Platform.ErrorHandling;

/// <summary>
/// Thrown when a request conflicts with the current state of a resource
/// (a duplicate, a stale update); mapped to a 409 problem response by
/// <see cref="ErrorMapper"/>.
/// </summary>
public sealed class ConflictException : Exception
{
    public ConflictException(string message)
        : base(message)
    {
    }
}
