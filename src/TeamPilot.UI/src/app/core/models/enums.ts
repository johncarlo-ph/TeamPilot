// String-literal unions mirroring the API's C# enums exactly (JsonStringEnumConverter
// serializes every enum as its member name, never a raw integer).

export type TicketStatus = 'ToDo' | 'InProgress' | 'ForReview' | 'Done' | 'Cancelled' | 'Blocked';

export const TICKET_STATUSES: TicketStatus[] = ['ToDo', 'InProgress', 'ForReview', 'Done', 'Cancelled', 'Blocked'];

export type TicketQuestionKind = 'Question' | 'Failure' | 'Decision';

export type TicketQuestionStatus = 'Pending' | 'Answered';

export type AgentRole = 'Research' | 'Design' | 'Coding' | 'Testing' | 'LiveAgent' | 'Custom';

export const AGENT_ROLES: AgentRole[] = ['Research', 'Design', 'Coding', 'Testing', 'LiveAgent', 'Custom'];

export type ChatMessageRole = 'User' | 'Assistant';

export type AgentStatus = 'Active' | 'Inactive';

export type AuditEventType =
  | 'LoginSucceeded'
  | 'LoginFailed'
  | 'Logout'
  | 'TokenReuseDetected'
  | 'ProjectCreated'
  | 'ProjectUpdated'
  | 'AgentConfigurationUpdated'
  | 'AgentStatusUpdated'
  | 'AgentInstructionUpdated'
  | 'AgentCreated'
  | 'WorkflowStageAdded'
  | 'WorkflowStageRemoved'
  | 'WorkflowReordered'
  | 'WorkflowLoopBackSet'
  | 'WorkflowLoopBackCleared'
  | 'InstructionTemplateCreated'
  | 'InstructionTemplateUpdated'
  | 'InstructionTemplateDeleted'
  | 'TicketCreated'
  | 'TicketPipelineStarted'
  | 'TicketMovedToReview'
  | 'TicketBlocked'
  | 'TicketQuestionAnswered'
  | 'TicketRetried'
  | 'ReviewSubmitted'
  | 'ConflictsDetected'
  | 'ConflictResolutionSuggested'
  | 'ConflictResolvedManually'
  | 'ConflictAiSuggestionAccepted'
  | 'GitBranchCreated'
  | 'UserRolesChanged'
  | 'UserStatusChanged'
  | 'UserProjectAssignmentsChanged';

export type ConflictStatus =
  | 'Detected'
  | 'AiResolutionSuggested'
  | 'ResolvedManually'
  | 'ResolvedWithAiSuggestion';

export type InstructionType = 'Constitution' | 'Guideline' | 'Requirement';

export const INSTRUCTION_TYPES: InstructionType[] = ['Constitution', 'Guideline', 'Requirement'];

export type ReviewDecision = 'Approve' | 'RequestChanges' | 'Reject' | 'ResolveConflict';

export type UserRole = 'Admin' | 'Analyst' | 'Developer';

export const USER_ROLES: UserRole[] = ['Admin', 'Analyst', 'Developer'];

export type UserStatus = 'Active' | 'Disabled';
