import { AgentRole } from './enums';
import { CommitDto } from './commit.model';
import { TicketDto } from './ticket.model';

export interface AgentWorkResultDto {
  ticketId: string;
  agentId: string;
  agentRole: AgentRole;
  llmOutput: string;
  commit: CommitDto | null;
}

export interface TicketPipelineResultDto {
  ticket: TicketDto;
  steps: AgentWorkResultDto[];
  testingPassed: boolean;
  testingAttempts: number;
}
