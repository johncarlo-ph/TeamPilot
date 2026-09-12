import { AgentRole, InstructionType } from './enums';

export interface InstructionTemplateDto {
  id: string;
  name: string;
  role: AgentRole;
  type: InstructionType;
  content: string;
  createdAtUtc: string;
  updatedAtUtc: string | null;
}

export interface CreateInstructionTemplateRequest {
  name: string;
  role: AgentRole;
  type: InstructionType;
  content: string;
}

export interface UpdateInstructionTemplateRequest {
  name: string;
  content: string;
}
