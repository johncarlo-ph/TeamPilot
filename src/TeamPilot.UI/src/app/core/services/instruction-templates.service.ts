import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  AgentRole,
  CreateInstructionTemplateRequest,
  InstructionTemplateDto,
  InstructionType,
  UpdateInstructionTemplateRequest,
} from '../models';

@Injectable({ providedIn: 'root' })
export class InstructionTemplatesService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/instruction-templates`;

  list(role?: AgentRole, type?: InstructionType): Observable<InstructionTemplateDto[]> {
    let params = new HttpParams();
    if (role) {
      params = params.set('role', role);
    }
    if (type) {
      params = params.set('type', type);
    }
    return this.http.get<InstructionTemplateDto[]>(this.baseUrl, { params });
  }

  getById(id: string): Observable<InstructionTemplateDto> {
    return this.http.get<InstructionTemplateDto>(`${this.baseUrl}/${id}`);
  }

  create(request: CreateInstructionTemplateRequest): Observable<InstructionTemplateDto> {
    return this.http.post<InstructionTemplateDto>(this.baseUrl, request);
  }

  update(id: string, request: UpdateInstructionTemplateRequest): Observable<InstructionTemplateDto> {
    return this.http.put<InstructionTemplateDto>(`${this.baseUrl}/${id}`, request);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }
}
