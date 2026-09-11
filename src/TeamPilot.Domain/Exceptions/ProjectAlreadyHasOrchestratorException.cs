namespace TeamPilot.Domain.Exceptions;

public sealed class ProjectAlreadyHasOrchestratorException : DomainException
{
    public Guid ProjectId { get; }

    public ProjectAlreadyHasOrchestratorException(Guid projectId)
        : base($"Project '{projectId}' already has an orchestrator agent. Each project may have only one.")
    {
        ProjectId = projectId;
    }
}
