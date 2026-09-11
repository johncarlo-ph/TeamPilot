import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ReviewDto, SubmitReviewRequest, TicketDto } from '../models';

@Injectable({ providedIn: 'root' })
export class ReviewsService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = environment.apiBaseUrl;

  listForTicket(ticketId: string): Observable<ReviewDto[]> {
    return this.http.get<ReviewDto[]>(`${this.apiBaseUrl}/tickets/${ticketId}/reviews`);
  }

  submit(ticketId: string, request: SubmitReviewRequest): Observable<TicketDto> {
    return this.http.post<TicketDto>(`${this.apiBaseUrl}/tickets/${ticketId}/reviews`, request);
  }
}
