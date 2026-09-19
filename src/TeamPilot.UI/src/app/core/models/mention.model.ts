export interface TicketMentionResultDto {
  id: string;
  title: string;
  projectId: string;
  projectName: string;
}

export interface ProjectMentionResultDto {
  id: string;
  name: string;
}

export interface FileMentionResultDto {
  path: string;
}

// A non-blocking "this referenced project needs a change" note a Research/Design stage raised
// (see backend TicketProjectInstruction) - read-only, for a human to act on manually.
export interface TicketProjectInstructionDto {
  id: string;
  agentId: string | null;
  referencedProjectId: string;
  text: string;
  createdAtUtc: string;
}
