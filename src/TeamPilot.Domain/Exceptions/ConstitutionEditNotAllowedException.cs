using TeamPilot.Domain.Enums;

namespace TeamPilot.Domain.Exceptions;

/// <summary>
/// Raised when a caller tries to add a new <see cref="InstructionType.Constitution"/> version for
/// a default/standing agent role (Research, Design, Coding, Testing, LiveAgent) that already has
/// one - i.e. an attempt to re-version the Constitution seeded by <see cref="AgentDefaultInstructions"/>
/// at <see cref="Agent.Create"/> time. Only an <see cref="AgentRole.Custom"/> agent's Constitution can
/// ever be edited - the default roles' identity and pipeline contract are fixed so the orchestration
/// pipeline can rely on what each stage fundamentally is.
/// </summary>
public sealed class ConstitutionEditNotAllowedException : DomainException
{
    public AgentRole Role { get; }

    public ConstitutionEditNotAllowedException(AgentRole role)
        : base($"The Constitution for a '{role}' agent is fixed and cannot be edited. Only a Custom agent's Constitution can be changed - adjust this agent's Guideline or Requirement instructions instead.")
    {
        Role = role;
    }
}
