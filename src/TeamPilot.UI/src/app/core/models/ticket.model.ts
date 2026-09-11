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
  createdAtUtc: string;
  updatedAtUtc: string | null;
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
  assignments: TicketAgentAssignmentDto[];
  commits: CommitDto[];
  reviews: ReviewDto[];
  conflicts: ConflictDto[];
  createdAtUtc: string;
  updatedAtUtc: string | null;
}

export interface CreateTicketRequest {
  title: string;
  description: string | null;
}

export interface CreateBranchRequest {
  ticketId: string;
  branchName: string;
}

export interface AssignSubAgentsRequest {
  agentIds: string[];
}
