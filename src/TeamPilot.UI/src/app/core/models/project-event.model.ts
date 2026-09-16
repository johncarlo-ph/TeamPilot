import { ProjectStatus } from './project.model';

export type ProjectEventType =
  | 'TicketChanged'
  | 'TicketQuestionChanged'
  | 'TicketAgentEventLogged'
  | 'ProjectCloneProgress';

/** Live clone progress/outcome, carried on a `ProjectCloneProgress` event - the one event type
 * with a real payload (see `ProjectEventDto`'s own doc comment). `totalObjects` is 0 while still
 * indeterminate. `status` is `'Cloning'` for every in-progress update and flips to `'Ready'`/
 * `'Failed'` on the final event, at which point `errorMessage` is populated for a failure. */
export interface CloneProgressPayload {
  receivedObjects: number;
  totalObjects: number;
  receivedBytes: number;
  status: ProjectStatus;
  errorMessage: string | null;
}

/** A refetch signal from `GET /api/projects/{projectId}/events` (SSE) - carries no state of its
 * own beyond what changed; consumers react by re-issuing the GET they already know how to make.
 * `cloneProgress` is the one deliberate exception (only populated for `'ProjectCloneProgress'`),
 * since there's no separate "fetch progress" endpoint to refetch from. */
export interface ProjectEventDto {
  type: ProjectEventType;
  projectId: string;
  ticketId: string | null;
  occurredAtUtc: string;
  cloneProgress: CloneProgressPayload | null;
}
