using FluentValidation;
using TeamPilot.Application.Projects.Dtos;

namespace TeamPilot.Application.Projects.Validators;

public sealed class CreateProjectRequestValidator : AbstractValidator<CreateProjectRequest>
{
    public CreateProjectRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.RemoteUrl).NotEmpty().MaximumLength(500)
            .Must(BeAnHttpsUrl).WithMessage("Remote URL must be a valid https:// URL.");
        RuleFor(x => x.AccessToken).NotEmpty().MaximumLength(500);
        RuleFor(x => x.BaseBranch).MaximumLength(200);
    }

    private static bool BeAnHttpsUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}
