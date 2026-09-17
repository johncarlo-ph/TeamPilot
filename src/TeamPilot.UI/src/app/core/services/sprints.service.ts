import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { CreateSprintRequest, SprintDto, UpdateSprintRequest } from '../models';

@Injectable({ providedIn: 'root' })
export class SprintsService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = environment.apiBaseUrl;

  list(projectId: string): Observable<SprintDto[]> {
    return this.http.get<SprintDto[]>(`${this.apiBaseUrl}/projects/${projectId}/sprints`);
  }

  getById(id: string): Observable<SprintDto> {
    return this.http.get<SprintDto>(`${this.apiBaseUrl}/sprints/${id}`);
  }

  create(projectId: string, request: CreateSprintRequest): Observable<SprintDto> {
    return this.http.post<SprintDto>(`${this.apiBaseUrl}/projects/${projectId}/sprints`, request);
  }

  update(id: string, request: UpdateSprintRequest): Observable<SprintDto> {
    return this.http.put<SprintDto>(`${this.apiBaseUrl}/sprints/${id}`, request);
  }

  remove(id: string): Observable<void> {
    return this.http.delete<void>(`${this.apiBaseUrl}/sprints/${id}`);
  }
}
