import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AnswerTicketQuestionRequest, TicketDto, TicketQuestionDto } from '../models';

@Injectable({ providedIn: 'root' })
export class TicketQuestionsService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = environment.apiBaseUrl;

  listForTicket(ticketId: string): Observable<TicketQuestionDto[]> {
    return this.http.get<TicketQuestionDto[]>(`${this.apiBaseUrl}/tickets/${ticketId}/questions`);
  }

  // Both run detached on the server (see IOrchestrationService.RunPipelineDetached) so a client
  // disconnecting can't abort the re-run - the returned ticket reflects the Unblock() that
  // happens right before it, not the eventual outcome.
  answer(ticketId: string, questionId: string, request: AnswerTicketQuestionRequest): Observable<TicketDto> {
    return this.http.post<TicketDto>(`${this.apiBaseUrl}/tickets/${ticketId}/questions/${questionId}/answer`, request);
  }

  retry(ticketId: string): Observable<TicketDto> {
    return this.http.post<TicketDto>(`${this.apiBaseUrl}/tickets/${ticketId}/retry`, {});
  }
}
