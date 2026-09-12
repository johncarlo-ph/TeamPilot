// String-literal unions mirroring the API's C# enums exactly (JsonStringEnumConverter
// serializes every enum as its member name, never a raw integer).

export type TicketStatus = 'ToDo' | 'InProgress' | 'ForReview' | 'Done' | 'Cancelled';

export const TICKET_STATUSES: TicketStatus[] = ['ToDo', 'InProgress', 'ForReview', 'Done', 'Cancelled'];

export type AgentRole = 'Research' | 'Design' | 'Coding' | 'Testing';

export const AGENT_ROLES: AgentRole[] = ['Research', 'Design', 'Coding', 'Testing'];

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
  | 'InstructionTemplateCreated'
  | 'InstructionTemplateUpdated'
  | 'InstructionTemplateDeleted'
  | 'TicketCreated'
  | 'TicketPipelineStarted'
  | 'TicketMovedToReview'
  | 'ReviewSubmitted'
  | 'ConflictsDetected'
  | 'ConflictResolutionSuggested'
  | 'ConflictResolvedManually'
  | 'ConflictAiSuggestionAccepted'
  | 'GitBranchCreated'
  | 'PipelineRunTriggered'
  | 'PipelineRunStarted'
  | 'PipelineRunCompleted'
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

export type PipelineRunStatus = 'Queued' | 'Running' | 'Succeeded' | 'Failed';

export type ReviewDecision = 'Approve' | 'RequestChanges' | 'ResolveConflict';

export type UserRole = 'Admin' | 'Analyst' | 'Developer';

export const USER_ROLES: UserRole[] = ['Admin', 'Analyst', 'Developer'];

export type UserStatus = 'Active' | 'Disabled';
