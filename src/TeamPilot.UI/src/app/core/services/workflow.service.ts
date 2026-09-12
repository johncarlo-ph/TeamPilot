import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  AddWorkflowStageRequest,
  AgentDto,
  CreateCustomAgentRequest,
  ReorderWorkflowRequest,
  SetWorkflowLoopBackRequest,
  WorkflowStageDto,
} from '../models';

@Injectable({ providedIn: 'root' })
export class WorkflowService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = environment.apiBaseUrl;

  private baseUrl(projectId: string): string {
    return `${this.apiBaseUrl}/projects/${projectId}/workflow`;
  }

  list(projectId: string): Observable<WorkflowStageDto[]> {
    return this.http.get<WorkflowStageDto[]>(this.baseUrl(projectId));
  }

  listUnscheduledAgents(projectId: string): Observable<AgentDto[]> {
    return this.http.get<AgentDto[]>(`${this.baseUrl(projectId)}/unscheduled-agents`);
  }

  createCustomAgent(projectId: string, request: CreateCustomAgentRequest): Observable<AgentDto> {
    return this.http.post<AgentDto>(`${this.baseUrl(projectId)}/agents`, request);
  }

  deleteCustomAgent(projectId: string, agentId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl(projectId)}/agents/${agentId}`);
  }

  addStage(projectId: string, request: AddWorkflowStageRequest): Observable<WorkflowStageDto> {
    return this.http.post<WorkflowStageDto>(`${this.baseUrl(projectId)}/stages`, request);
  }

  removeStage(projectId: string, stageId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl(projectId)}/stages/${stageId}`);
  }

  reorder(projectId: string, request: ReorderWorkflowRequest): Observable<WorkflowStageDto[]> {
    return this.http.put<WorkflowStageDto[]>(`${this.baseUrl(projectId)}/order`, request);
  }

  setLoopBack(projectId: string, stageId: string, request: SetWorkflowLoopBackRequest): Observable<WorkflowStageDto> {
    return this.http.put<WorkflowStageDto>(`${this.baseUrl(projectId)}/stages/${stageId}/loop-back`, request);
  }

  clearLoopBack(projectId: string, stageId: string): Observable<WorkflowStageDto> {
    return this.http.delete<WorkflowStageDto>(`${this.baseUrl(projectId)}/stages/${stageId}/loop-back`);
  }
}
