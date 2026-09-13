namespace TeamPilot.Application.Common.Exceptions;

/// <summary>
/// Thrown when a call to the LLM provider's API fails after the resilience pipeline's retries
/// are exhausted (timeout, unreachable host, non-success response). Not a
/// <see cref="TeamPilot.Domain.Exceptions.DomainException"/> - this is an external-system
/// failure, not a domain invariant violation. Maps to HTTP 422.
/// </summary>
public sealed class LlmOperationException : Exception
{
    public LlmOperationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
