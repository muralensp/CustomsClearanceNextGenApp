namespace CustomsClearance.Platform.ErrorHandling;

/// <summary>
/// Thrown when a requested resource does not exist; mapped to a 404
/// problem response by <see cref="ErrorMapper"/>.
/// </summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException(string message)
        : base(message)
    {
    }
}
