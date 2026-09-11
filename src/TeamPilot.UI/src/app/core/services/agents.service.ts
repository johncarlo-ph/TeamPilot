import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  AgentDto,
  CreateAgentRequest,
  UpdateAgentConfigurationRequest,
  UpdateAgentStatusRequest,
} from '../models';

@Injectable({ providedIn: 'root' })
export class AgentsService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = environment.apiBaseUrl;

  listForProject(projectId: string): Observable<AgentDto[]> {
    return this.http.get<AgentDto[]>(`${this.apiBaseUrl}/projects/${projectId}/agents`);
  }

  create(projectId: string, request: CreateAgentRequest): Observable<AgentDto> {
    return this.http.post<AgentDto>(`${this.apiBaseUrl}/projects/${projectId}/agents`, request);
  }

  getById(id: string): Observable<AgentDto> {
    return this.http.get<AgentDto>(`${this.apiBaseUrl}/agents/${id}`);
  }

  updateConfiguration(id: string, request: UpdateAgentConfigurationRequest): Observable<AgentDto> {
    return this.http.put<AgentDto>(`${this.apiBaseUrl}/agents/${id}/configuration`, request);
  }

  updateStatus(id: string, request: UpdateAgentStatusRequest): Observable<AgentDto> {
    return this.http.put<AgentDto>(`${this.apiBaseUrl}/agents/${id}/status`, request);
  }
}
