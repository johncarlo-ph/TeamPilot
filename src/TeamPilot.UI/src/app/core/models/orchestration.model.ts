import { AgentRole } from './enums';
import { CommitDto } from './commit.model';

export interface AgentWorkResultDto {
  ticketId: string;
  agentId: string;
  agentRole: AgentRole;
  llmOutput: string;
  commit: CommitDto | null;
}
