import { InstructionType } from './enums';

export interface InstructionDto {
  id: string;
  agentId: string;
  type: InstructionType;
  content: string;
  version: number;
  isCurrent: boolean;
  createdBy: string | null;
  createdAtUtc: string;
}

export interface AddInstructionVersionRequest {
  type: InstructionType;
  content: string;
  updatedBy: string | null;
}
