using FluentValidation;
using ValidationException = TeamPilot.Application.Common.Exceptions.ValidationException;

namespace TeamPilot.Application.Common.Extensions;

public static class ValidatorExtensions
{
    /// <summary>
    /// Validates <paramref name="instance"/> and throws the Application layer's own
    /// <see cref="ValidationException"/> (not FluentValidation's) on failure, so callers
    /// never need to reference FluentValidation's exception type directly.
    /// </summary>
    public static async Task EnsureValidAsync<T>(this IValidator<T> validator, T instance, CancellationToken cancellationToken = default)
    {
        var result = await validator.ValidateAsync(instance, cancellationToken);

        if (!result.IsValid)
        {
            throw new ValidationException(result.Errors);
        }
    }
}
