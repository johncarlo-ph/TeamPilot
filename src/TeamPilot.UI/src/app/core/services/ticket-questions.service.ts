import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AnswerTicketQuestionRequest, TicketPipelineResultDto, TicketQuestionDto } from '../models';

@Injectable({ providedIn: 'root' })
export class TicketQuestionsService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = environment.apiBaseUrl;

  listForTicket(ticketId: string): Observable<TicketQuestionDto[]> {
    return this.http.get<TicketQuestionDto[]>(`${this.apiBaseUrl}/tickets/${ticketId}/questions`);
  }

  answer(ticketId: string, questionId: string, request: AnswerTicketQuestionRequest): Observable<TicketPipelineResultDto> {
    return this.http.post<TicketPipelineResultDto>(`${this.apiBaseUrl}/tickets/${ticketId}/questions/${questionId}/answer`, request);
  }

  retry(ticketId: string): Observable<TicketPipelineResultDto> {
    return this.http.post<TicketPipelineResultDto>(`${this.apiBaseUrl}/tickets/${ticketId}/retry`, {});
  }
}
