namespace TeamPilot.Application.Common.Exceptions;

/// <summary>
/// Thrown when a clone/push/fetch against a project's remote repository fails (bad URL,
/// bad or expired token, unreachable host). Not a <see cref="TeamPilot.Domain.Exceptions.DomainException"/> -
/// this is an external-system failure, not a domain invariant violation. Maps to HTTP 422.
/// </summary>
public sealed class GitOperationException : Exception
{
    public GitOperationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
