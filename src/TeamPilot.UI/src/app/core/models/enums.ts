// String-literal unions mirroring the API's C# enums exactly (JsonStringEnumConverter
// serializes every enum as its member name, never a raw integer).

export type TicketStatus = 'ToDo' | 'InProgress' | 'ForReview' | 'Done';

export const TICKET_STATUSES: TicketStatus[] = ['ToDo', 'InProgress', 'ForReview', 'Done'];

export type AgentRole = 'Orchestrator' | 'Research' | 'Design' | 'Coding';

export const AGENT_ROLES: AgentRole[] = ['Orchestrator', 'Research', 'Design', 'Coding'];

export type AgentStatus = 'Active' | 'Inactive';

export type AuditEventType =
  | 'LoginSucceeded'
  | 'LoginFailed'
  | 'Logout'
  | 'TokenRefreshed'
  | 'TokenReuseDetected';

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
