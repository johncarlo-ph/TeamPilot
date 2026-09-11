import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  CompletePipelineRunRequest,
  PipelineRunDto,
  TriggerPipelineRunRequest,
} from '../models';

@Injectable({ providedIn: 'root' })
export class PipelineRunsService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = environment.apiBaseUrl;

  listForProject(projectId: string): Observable<PipelineRunDto[]> {
    return this.http.get<PipelineRunDto[]>(`${this.apiBaseUrl}/projects/${projectId}/pipeline-runs`);
  }

  trigger(projectId: string, request: TriggerPipelineRunRequest): Observable<PipelineRunDto> {
    return this.http.post<PipelineRunDto>(
      `${this.apiBaseUrl}/projects/${projectId}/pipeline-runs`,
      request
    );
  }

  start(id: string): Observable<PipelineRunDto> {
    return this.http.post<PipelineRunDto>(`${this.apiBaseUrl}/pipeline-runs/${id}/start`, null);
  }

  complete(id: string, request: CompletePipelineRunRequest): Observable<PipelineRunDto> {
    return this.http.post<PipelineRunDto>(
      `${this.apiBaseUrl}/pipeline-runs/${id}/complete`,
      request
    );
  }
}
