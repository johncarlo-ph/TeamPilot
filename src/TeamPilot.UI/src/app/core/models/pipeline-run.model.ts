import { PipelineRunStatus } from './enums';

export interface PipelineRunDto {
  id: string;
  projectId: string;
  ticketId: string | null;
  status: PipelineRunStatus;
  triggerReason: string;
  logOutput: string | null;
  startedAtUtc: string | null;
  completedAtUtc: string | null;
  createdAtUtc: string;
}

export interface TriggerPipelineRunRequest {
  triggerReason: string;
}

export interface CompletePipelineRunRequest {
  succeeded: boolean;
  logOutput: string | null;
}
