namespace TeamPilot.Domain.Exceptions;

/// <summary>
/// Base type for exceptions raised when a domain invariant or state-transition rule is violated.
/// </summary>
public abstract class DomainException : Exception
{
    protected DomainException(string message)
        : base(message)
    {
    }
}
