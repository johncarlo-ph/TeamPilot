using FluentValidation;
using TeamPilot.Application.Workflow.Dtos;

namespace TeamPilot.Application.Workflow.Validators;

public sealed class ReorderWorkflowRequestValidator : AbstractValidator<ReorderWorkflowRequest>
{
    public ReorderWorkflowRequestValidator()
    {
        RuleFor(x => x.StageIds).NotEmpty();
    }
}
