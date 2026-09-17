import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  CancelTicketRequest,
  CreateTicketRequest,
  TicketDetailDto,
  TicketDto,
  TicketStatus,
} from '../models';

@Injectable({ providedIn: 'root' })
export class TicketsService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = environment.apiBaseUrl;

  listForSprint(sprintId: string, status?: TicketStatus): Observable<TicketDto[]> {
    let params = new HttpParams();
    if (status) {
      params = params.set('status', status);
    }
    return this.http.get<TicketDto[]>(`${this.apiBaseUrl}/sprints/${sprintId}/tickets`, { params });
  }

  /** Every ticket across a project's sprints - used by the Agents page's workflow-lock check,
   * a project-wide concern rather than one sprint's board. */
  listForProject(projectId: string, status?: TicketStatus): Observable<TicketDto[]> {
    let params = new HttpParams();
    if (status) {
      params = params.set('status', status);
    }
    return this.http.get<TicketDto[]>(`${this.apiBaseUrl}/projects/${projectId}/tickets`, { params });
  }

  create(sprintId: string, request: CreateTicketRequest): Observable<TicketDto> {
    return this.http.post<TicketDto>(`${this.apiBaseUrl}/sprints/${sprintId}/tickets`, request);
  }

  getById(id: string): Observable<TicketDetailDto> {
    return this.http.get<TicketDetailDto>(`${this.apiBaseUrl}/tickets/${id}`);
  }

  // Runs detached on the server (see IOrchestrationService.StartPipelineAsync) so a client
  // disconnecting (e.g. a page refresh) can't abort an in-flight run - the returned ticket may
  // still show ToDo; poll (listForProject/getById) to observe the eventual outcome.
  startPipeline(id: string): Observable<TicketDto> {
    return this.http.post<TicketDto>(`${this.apiBaseUrl}/tickets/${id}/start`, null);
  }

  moveToReview(id: string): Observable<TicketDto> {
    return this.http.post<TicketDto>(`${this.apiBaseUrl}/tickets/${id}/move-to-review`, null);
  }

  cancel(id: string, request: CancelTicketRequest): Observable<TicketDto> {
    return this.http.post<TicketDto>(`${this.apiBaseUrl}/tickets/${id}/cancel`, request);
  }
}
