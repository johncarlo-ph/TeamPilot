import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { TicketAgentEventDto } from '../models';

@Injectable({ providedIn: 'root' })
export class TicketAgentEventsService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = environment.apiBaseUrl;

  listForTicket(ticketId: string): Observable<TicketAgentEventDto[]> {
    return this.http.get<TicketAgentEventDto[]>(`${this.apiBaseUrl}/tickets/${ticketId}/agent-events`);
  }
}
