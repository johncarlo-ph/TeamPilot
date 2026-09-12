namespace TeamPilot.Application.Common.Exceptions;

/// <summary>
/// Thrown when trying to add an agent to a project's workflow before it has a current
/// Constitution, Guideline, and Requirement instruction - see
/// <see cref="Domain.Entities.Agent.HasCompleteInstructions"/>.
/// </summary>
public sealed class AgentInstructionsIncompleteException(string agentName)
    : Exception($"Agent '{agentName}' needs Constitution, Guideline, and Requirement instructions before it can be added to the pipeline.")
{
}
