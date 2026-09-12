namespace TeamPilot.Domain.Enums;

public enum AgentRole
{
    Research,
    Design,
    Coding,
    Testing,

    /// <summary>
    /// The standing, chat-capable agent every project has exactly one of (see
    /// <see cref="TeamPilot.Application.Agents.IAgentService.EnsureLiveAgentAsync"/>) - unlike
    /// the pipeline roles above, it isn't invoked automatically per ticket. A human converses
    /// with it directly to ask questions, have code/business rules explained, and draft tickets.
    /// </summary>
    LiveAgent,

    /// <summary>
    /// An admin-created agent with no seeded instructions (see
    /// <see cref="TeamPilot.Domain.Entities.AgentDefaultInstructions.For"/>) and no repository
    /// write access - it can only be added to a project's workflow as a prompt-only stage, never
    /// as one that commits to Git (that stays exclusive to <see cref="Coding"/>).
    /// </summary>
    Custom
}
