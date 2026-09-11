namespace TeamPilot.Application.Common.Exceptions;

/// <summary>
/// Thrown when an authenticated user is not allowed to perform the requested operation
/// (e.g. no access to the project, or the wrong role for the action). Maps to HTTP 403.
/// </summary>
public sealed class ForbiddenException : Exception
{
    public ForbiddenException(string message)
        : base(message)
    {
    }
}
