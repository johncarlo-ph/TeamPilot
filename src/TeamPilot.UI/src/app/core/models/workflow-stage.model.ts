import { AgentDto } from './agent.model';

export interface WorkflowStageDto {
  id: string;
  projectId: string;
  order: number;
  agent: AgentDto;
  loopBackToStageId: string | null;
  maxLoopIterations: number | null;
}

export interface CreateCustomAgentRequest {
  name: string;
}

export interface AddWorkflowStageRequest {
  agentId: string;
}

export interface ReorderWorkflowRequest {
  stageIds: string[];
}

export interface SetWorkflowLoopBackRequest {
  targetStageId: string;
  maxLoopIterations: number;
}
