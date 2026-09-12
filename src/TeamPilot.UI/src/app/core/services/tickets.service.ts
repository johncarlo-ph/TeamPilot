import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  CancelTicketRequest,
  CreateTicketRequest,
  TicketDetailDto,
  TicketDto,
  TicketPipelineResultDto,
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

  startPipeline(id: string): Observable<TicketPipelineResultDto> {
    return this.http.post<TicketPipelineResultDto>(`${this.apiBaseUrl}/tickets/${id}/start`, null);
  }

  moveToReview(id: string): Observable<TicketDto> {
    return this.http.post<TicketDto>(`${this.apiBaseUrl}/tickets/${id}/move-to-review`, null);
  }

  cancel(id: string, request: CancelTicketRequest): Observable<TicketDto> {
    return this.http.post<TicketDto>(`${this.apiBaseUrl}/tickets/${id}/cancel`, request);
  }
}
