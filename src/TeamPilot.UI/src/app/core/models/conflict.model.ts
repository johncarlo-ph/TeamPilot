import { ConflictStatus } from './enums';

export interface ConflictDto {
  id: string;
  ticketId: string;
  commitId: string | null;
  filePath: string;
  conflictingDiffContent: string;
  aiSuggestedResolution: string | null;
  resolvedContent: string | null;
  resolutionNote: string | null;
  status: ConflictStatus;
  resolvedAtUtc: string | null;
  resolvedBy: string | null;
  baseTipSha: string | null;
  createdAtUtc: string;
}

export interface ResolveConflictManuallyRequest {
  resolvedContent: string;
  note: string | null;
  resolvedBy: string;
}

export interface AcceptAiSuggestionRequest {
  resolvedBy: string;
}
