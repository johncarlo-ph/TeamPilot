using FluentValidation.Results;

namespace TeamPilot.Application.Common.Exceptions;

/// <summary>
/// Thrown when a request fails FluentValidation rules. Carries per-field error messages
/// so the Presentation layer can shape a consistent 400 response.
/// </summary>
public sealed class ValidationException : Exception
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public ValidationException()
        : base("One or more validation failures have occurred.")
    {
        Errors = new Dictionary<string, string[]>();
    }

    public ValidationException(IEnumerable<ValidationFailure> failures)
        : this()
    {
        Errors = failures
            .GroupBy(f => f.PropertyName, f => f.ErrorMessage)
            .ToDictionary(g => g.Key, g => g.ToArray());
    }
}
