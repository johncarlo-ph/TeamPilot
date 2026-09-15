import { AgentRole, TicketAgentEventKind } from './enums';

export interface TicketAgentEventDto {
  id: string;
  ticketId: string;
  agentId: string | null;
  role: AgentRole | null;
  kind: TicketAgentEventKind;
  result: string | null;
  createdAtUtc: string;
}
