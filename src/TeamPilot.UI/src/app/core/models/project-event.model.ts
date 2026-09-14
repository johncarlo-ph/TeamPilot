export type ProjectEventType = 'TicketChanged' | 'TicketQuestionChanged';

/** A refetch signal from `GET /api/projects/{projectId}/events` (SSE) - carries no state of its
 * own beyond what changed; consumers react by re-issuing the GET they already know how to make. */
export interface ProjectEventDto {
  type: ProjectEventType;
  projectId: string;
  ticketId: string | null;
  occurredAtUtc: string;
}
