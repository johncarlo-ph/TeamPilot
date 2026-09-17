/** Mirrors the API's TicketStatusCountsDto - board-relevant statuses only, Cancelled excluded
 * (matches BOARD_COLUMNS in features/board/board.ts). */
export interface TicketStatusCountsDto {
  toDo: number;
  inProgress: number;
  blocked: number;
  forReview: number;
  done: number;
}

export interface SprintDto {
  id: string;
  projectId: string;
  name: string;
  baseBranch: string;
  sprintStartDate: string | null;
  sprintEndDate: string | null;
  sprintGoal: string | null;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  ticketStatusCounts: TicketStatusCountsDto;
}

export interface CreateSprintRequest {
  name: string;
  baseBranch: string | null;
  sprintStartDate: string | null;
  sprintEndDate: string | null;
  sprintGoal: string | null;
}

export interface UpdateSprintRequest {
  name: string;
  baseBranch: string;
  sprintStartDate: string | null;
  sprintEndDate: string | null;
  sprintGoal: string | null;
}
