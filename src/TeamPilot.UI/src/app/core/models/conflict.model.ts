import { ConflictStatus } from './enums';

export interface ConflictDto {
  id: string;
  ticketId: string;
  commitId: string | null;
  filePath: string;
  conflictingDiffContent: string;
  aiSuggestedResolution: string | null;
  resolutionNote: string | null;
  status: ConflictStatus;
  resolvedAtUtc: string | null;
  resolvedBy: string | null;
  createdAtUtc: string;
}

export interface ResolveConflictManuallyRequest {
  note: string;
  resolvedBy: string;
}

export interface AcceptAiSuggestionRequest {
  resolvedBy: string;
}
