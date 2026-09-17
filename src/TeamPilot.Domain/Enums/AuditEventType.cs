namespace TeamPilot.Domain.Enums;

/// <summary>
/// Every event the audit log can record. Deliberately excludes <c>TokenRefreshed</c> - a
/// refresh happens silently in the background on a timer, not as a result of anything a user
/// did, so logging it would just be noise between the login and logout entries that actually
/// matter.
/// </summary>
public enum AuditEventType
{
    LoginSucceeded,
    LoginFailed,
    Logout,
    TokenReuseDetected,
    ProjectCreated,
    ProjectCloneFailed,
    ProjectUpdated,
    ProjectRemoved,
    SprintCreated,
    SprintUpdated,
    SprintRemoved,
    AgentConfigurationUpdated,
    AgentStatusUpdated,
    AgentInstructionUpdated,
    AgentCreated,
    AgentDeleted,
    WorkflowStageAdded,
    WorkflowStageRemoved,
    WorkflowReordered,
    WorkflowLoopBackSet,
    WorkflowLoopBackCleared,
    InstructionTemplateCreated,
    InstructionTemplateUpdated,
    InstructionTemplateDeleted,
    TicketCreated,
    TicketAssignedToSprint,
    TicketMovedToBacklog,
    TicketPipelineStarted,
    TicketMovedToReview,
    TicketCancelled,
    TicketBlocked,
    TicketQuestionAnswered,
    TicketRetried,
    ReviewSubmitted,
    ConflictsDetected,
    ConflictResolutionSuggested,
    ConflictResolvedManually,
    ConflictAiSuggestionAccepted,
    GitBranchCreated,
    GitBranchDeleted,
    UserRolesChanged,
    UserStatusChanged,
    UserProjectAssignmentsChanged,
}
