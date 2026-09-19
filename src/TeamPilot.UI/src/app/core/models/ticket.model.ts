import { AgentRole, TicketStatus } from './enums';
import { CommitDto } from './commit.model';
import { ReviewDto } from './review.model';
import { ConflictDto } from './conflict.model';
import { TicketProjectInstructionDto } from './mention.model';
import { TicketPipelineNoteDto } from './ticket-pipeline-note.model';

export interface TicketDto {
  id: string;
  projectId: string;
  // Null while the ticket sits in the project's backlog, not yet assigned to a sprint.
  sprintId: string | null;
  title: string;
  description: string;
  acceptanceCriteria: string;
  status: TicketStatus;
  branchName: string | null;
  cancellationReason: string | null;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  // Whether a pipeline run is actually executing for this ticket right now - status alone can't
  // tell: a ticket sits InProgress both while a run is actively executing and while it's idle,
  // waiting for a human to manually trigger one (e.g. via "Run Pipeline").
  pipelineRunning: boolean;
}

export interface TicketAgentAssignmentDto {
  agentId: string;
  roleAtAssignment: AgentRole;
  assignedAtUtc: string;
}

export interface TicketDetailDto {
  id: string;
  projectId: string;
  sprintId: string | null;
  title: string;
  description: string;
  acceptanceCriteria: string;
  status: TicketStatus;
  branchName: string | null;
  cancellationReason: string | null;
  assignments: TicketAgentAssignmentDto[];
  commits: CommitDto[];
  reviews: ReviewDto[];
  conflicts: ConflictDto[];
  instructions: TicketProjectInstructionDto[];
  pipelineNotes: TicketPipelineNoteDto[];
  createdAtUtc: string;
  updatedAtUtc: string | null;
  pipelineRunning: boolean;
}

export interface CreateTicketRequest {
  title: string;
  description: string | null;
  acceptanceCriteria: string;
}

export interface CreateBranchRequest {
  ticketId: string;
  branchName: string;
}

export interface CancelTicketRequest {
  reason: string | null;
}

export interface AssignTicketToSprintRequest {
  sprintId: string;
}
