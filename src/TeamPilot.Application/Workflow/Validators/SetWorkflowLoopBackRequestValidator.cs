using FluentValidation;
using TeamPilot.Application.Workflow.Dtos;

namespace TeamPilot.Application.Workflow.Validators;

public sealed class SetWorkflowLoopBackRequestValidator : AbstractValidator<SetWorkflowLoopBackRequest>
{
    public SetWorkflowLoopBackRequestValidator()
    {
        RuleFor(x => x.TargetStageId).NotEmpty();
        RuleFor(x => x.MaxLoopIterations).GreaterThan(0);
    }
}
