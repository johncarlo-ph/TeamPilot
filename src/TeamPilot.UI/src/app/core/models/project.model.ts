/** Mirrors the API's TicketStatusCountsDto - board-relevant statuses only, Cancelled excluded
 * (matches BOARD_COLUMNS in features/board/board.ts). */
export interface TicketStatusCountsDto {
  toDo: number;
  inProgress: number;
  blocked: number;
  forReview: number;
  done: number;
}

/** Where a project is in cloning its remote repository - see `ProjectCard`'s progress UI, driven
 * by `ProjectEventType: 'ProjectCloneProgress'`. */
export type ProjectStatus = 'Cloning' | 'Ready' | 'Failed';

export interface ProjectDto {
  id: string;
  name: string;
  description: string;
  remoteUrl: string;
  baseBranch: string;
  status: ProjectStatus;
  cloneFailureReason: string | null;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  ticketStatusCounts: TicketStatusCountsDto;
  sprintStartDate: string | null;
  sprintEndDate: string | null;
  sprintGoal: string | null;
}

export interface CreateProjectRequest {
  name: string;
  description: string | null;
  remoteUrl: string;
  accessToken: string;
  baseBranch: string | null;
  sprintStartDate: string | null;
  sprintEndDate: string | null;
  sprintGoal: string | null;
}

/** accessToken null/blank keeps the currently stored token - remoteUrl isn't included here
 * since it's immutable after creation (re-pointing it would orphan the existing clone). */
export interface UpdateProjectRequest {
  name: string;
  description: string | null;
  accessToken: string | null;
  baseBranch: string;
  sprintStartDate: string | null;
  sprintEndDate: string | null;
  sprintGoal: string | null;
}
