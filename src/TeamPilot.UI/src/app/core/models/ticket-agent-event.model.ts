import { AgentRole, TicketAgentEventKind } from './enums';

export interface TicketAgentEventDto {
  id: string;
  ticketId: string;
  agentId: string | null;
  role: AgentRole | null;
  kind: TicketAgentEventKind;
  result: string | null;
  inputTokens: number | null;
  outputTokens: number | null;
  durationMs: number | null;
  createdAtUtc: string;
}
