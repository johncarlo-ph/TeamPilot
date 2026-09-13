import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ChatMessageDto, SendChatMessageRequest } from '../models';

@Injectable({ providedIn: 'root' })
export class LiveAgentChatService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = environment.apiBaseUrl;

  listMessages(projectId: string): Observable<ChatMessageDto[]> {
    return this.http.get<ChatMessageDto[]>(`${this.apiBaseUrl}/projects/${projectId}/live-agent/messages`);
  }

  sendMessage(projectId: string, request: SendChatMessageRequest): Observable<ChatMessageDto> {
    return this.http.post<ChatMessageDto>(`${this.apiBaseUrl}/projects/${projectId}/live-agent/messages`, request);
  }

  approveTicket(projectId: string, messageId: string): Observable<ChatMessageDto> {
    return this.http.post<ChatMessageDto>(
      `${this.apiBaseUrl}/projects/${projectId}/live-agent/messages/${messageId}/approve-ticket`,
      null
    );
  }

  rejectTicket(projectId: string, messageId: string): Observable<ChatMessageDto> {
    return this.http.post<ChatMessageDto>(
      `${this.apiBaseUrl}/projects/${projectId}/live-agent/messages/${messageId}/reject-ticket`,
      null
    );
  }
}
