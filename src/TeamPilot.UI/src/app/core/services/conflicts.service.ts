import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AcceptAiSuggestionRequest, ConflictDto, ResolveConflictManuallyRequest } from '../models';

@Injectable({ providedIn: 'root' })
export class ConflictsService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = environment.apiBaseUrl;

  listForTicket(ticketId: string): Observable<ConflictDto[]> {
    return this.http.get<ConflictDto[]>(`${this.apiBaseUrl}/tickets/${ticketId}/conflicts`);
  }

  detect(ticketId: string): Observable<ConflictDto[]> {
    return this.http.post<ConflictDto[]>(
      `${this.apiBaseUrl}/tickets/${ticketId}/conflicts/detect`,
      null
    );
  }

  suggestResolution(id: string): Observable<ConflictDto> {
    return this.http.post<ConflictDto>(`${this.apiBaseUrl}/conflicts/${id}/suggest-resolution`, null);
  }

  resolveManually(id: string, request: ResolveConflictManuallyRequest): Observable<ConflictDto> {
    return this.http.post<ConflictDto>(
      `${this.apiBaseUrl}/conflicts/${id}/resolve-manually`,
      request
    );
  }

  acceptAiSuggestion(id: string, request: AcceptAiSuggestionRequest): Observable<ConflictDto> {
    return this.http.post<ConflictDto>(
      `${this.apiBaseUrl}/conflicts/${id}/accept-ai-suggestion`,
      request
    );
  }
}
