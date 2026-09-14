import { AgentRole, TicketStatus } from './enums';
import { CommitDto } from './commit.model';
import { ReviewDto } from './review.model';
import { ConflictDto } from './conflict.model';

export interface TicketDto {
  id: string;
  projectId: string;
  title: string;
  description: string;
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
  title: string;
  description: string;
  status: TicketStatus;
  branchName: string | null;
  cancellationReason: string | null;
  assignments: TicketAgentAssignmentDto[];
  commits: CommitDto[];
  reviews: ReviewDto[];
  conflicts: ConflictDto[];
  createdAtUtc: string;
  updatedAtUtc: string | null;
  pipelineRunning: boolean;
}

export interface CreateTicketRequest {
  title: string;
  description: string | null;
}

export interface CreateBranchRequest {
  ticketId: string;
  branchName: string;
}

export interface CancelTicketRequest {
  reason: string | null;
}
