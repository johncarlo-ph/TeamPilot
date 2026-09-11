import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  AgentWorkResultDto,
  AssignSubAgentsRequest,
  CreateTicketRequest,
  TicketDetailDto,
  TicketDto,
  TicketStatus,
} from '../models';

@Injectable({ providedIn: 'root' })
export class TicketsService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = environment.apiBaseUrl;

  listForProject(projectId: string, status?: TicketStatus): Observable<TicketDto[]> {
    let params = new HttpParams();
    if (status) {
      params = params.set('status', status);
    }
    return this.http.get<TicketDto[]>(`${this.apiBaseUrl}/projects/${projectId}/tickets`, { params });
  }

  create(projectId: string, request: CreateTicketRequest): Observable<TicketDto> {
    return this.http.post<TicketDto>(`${this.apiBaseUrl}/projects/${projectId}/tickets`, request);
  }

  getById(id: string): Observable<TicketDetailDto> {
    return this.http.get<TicketDetailDto>(`${this.apiBaseUrl}/tickets/${id}`);
  }

  assignAgents(id: string, request: AssignSubAgentsRequest): Observable<TicketDto> {
    return this.http.post<TicketDto>(`${this.apiBaseUrl}/tickets/${id}/assign-agents`, request);
  }

  executeAgent(ticketId: string, agentId: string): Observable<AgentWorkResultDto> {
    return this.http.post<AgentWorkResultDto>(
      `${this.apiBaseUrl}/tickets/${ticketId}/agents/${agentId}/execute`,
      null
    );
  }

  moveToReview(id: string): Observable<TicketDto> {
    return this.http.post<TicketDto>(`${this.apiBaseUrl}/tickets/${id}/move-to-review`, null);
  }
}
