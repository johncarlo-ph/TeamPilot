import { AgentRole } from './enums';

// A non-blocking human-facing note any pipeline stage chose to leave behind (see backend
// TicketPipelineNote and the NOTES: marker every stage's prompt offers) - a summary of what it
// did, an assumption it made, a limitation, or a suggested follow-up. Read-only, most useful once
// the ticket reaches Done and there's no more raw agent output to dig through.
export interface TicketPipelineNoteDto {
  id: string;
  agentId: string | null;
  role: AgentRole | null;
  text: string;
  createdAtUtc: string;
}
