import { AgentRole, AgentStatus } from './enums';

export interface AgentDto {
  id: string;
  projectId: string;
  name: string;
  role: AgentRole;
  status: AgentStatus;
  configurationJson: string;
  createdAtUtc: string;
  updatedAtUtc: string | null;
}

export interface UpdateAgentConfigurationRequest {
  configurationJson: string;
}

export interface UpdateAgentStatusRequest {
  status: AgentStatus;
}
