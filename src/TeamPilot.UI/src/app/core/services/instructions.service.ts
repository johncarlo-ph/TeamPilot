import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AddInstructionVersionRequest, InstructionDto, InstructionType } from '../models';

@Injectable({ providedIn: 'root' })
export class InstructionsService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = environment.apiBaseUrl;

  listForAgent(agentId: string): Observable<InstructionDto[]> {
    return this.http.get<InstructionDto[]>(`${this.apiBaseUrl}/agents/${agentId}/instructions`);
  }

  addVersion(agentId: string, request: AddInstructionVersionRequest): Observable<InstructionDto> {
    return this.http.post<InstructionDto>(
      `${this.apiBaseUrl}/agents/${agentId}/instructions`,
      request
    );
  }

  getCurrent(agentId: string, type: InstructionType): Observable<InstructionDto> {
    const params = new HttpParams().set('type', type);
    return this.http.get<InstructionDto>(
      `${this.apiBaseUrl}/agents/${agentId}/instructions/current`,
      { params }
    );
  }
}
