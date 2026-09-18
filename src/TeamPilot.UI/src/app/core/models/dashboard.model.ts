import { TicketStatus } from './enums';
import { TicketStatusCountsDto } from './sprint.model';

/** SprintId/SprintName are null when the ticket is still in the project's backlog. */
export interface AttentionTicketDto {
  id: string;
  title: string;
  status: TicketStatus;
  projectId: string;
  projectName: string;
  sprintId: string | null;
  sprintName: string | null;
}

export interface AtRiskSprintDto {
  id: string;
  name: string;
  projectId: string;
  projectName: string;
  sprintEndDate: string;
  unfinishedTicketCount: number;
}

export interface NeedsAttentionDto {
  blockedTickets: AttentionTicketDto[];
  ticketsForReview: AttentionTicketDto[];
  sprintsAtRisk: AtRiskSprintDto[];
}

export interface DashboardSummaryDto {
  activeSprintCount: number;
  ticketStatusCounts: TicketStatusCountsDto;
  needsAttention: NeedsAttentionDto;
}
