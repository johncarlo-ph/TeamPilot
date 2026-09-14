import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  ChatMessageDto,
  ConversationDto,
  CreateConversationRequest,
  RenameConversationRequest,
  SendChatMessageRequest,
} from '../models';

@Injectable({ providedIn: 'root' })
export class LiveAgentChatService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = environment.apiBaseUrl;

  listConversations(projectId: string): Observable<ConversationDto[]> {
    return this.http.get<ConversationDto[]>(`${this.apiBaseUrl}/projects/${projectId}/live-agent/conversations`);
  }

  createConversation(projectId: string, request: CreateConversationRequest): Observable<ConversationDto> {
    return this.http.post<ConversationDto>(`${this.apiBaseUrl}/projects/${projectId}/live-agent/conversations`, request);
  }

  renameConversation(projectId: string, conversationId: string, request: RenameConversationRequest): Observable<ConversationDto> {
    return this.http.put<ConversationDto>(
      `${this.apiBaseUrl}/projects/${projectId}/live-agent/conversations/${conversationId}`,
      request
    );
  }

  listMessages(projectId: string, conversationId: string): Observable<ChatMessageDto[]> {
    return this.http.get<ChatMessageDto[]>(
      `${this.apiBaseUrl}/projects/${projectId}/live-agent/conversations/${conversationId}/messages`
    );
  }

  sendMessage(projectId: string, conversationId: string, request: SendChatMessageRequest): Observable<ChatMessageDto> {
    return this.http.post<ChatMessageDto>(
      `${this.apiBaseUrl}/projects/${projectId}/live-agent/conversations/${conversationId}/messages`,
      request
    );
  }

  approveTicket(projectId: string, conversationId: string, messageId: string): Observable<ChatMessageDto> {
    return this.http.post<ChatMessageDto>(
      `${this.apiBaseUrl}/projects/${projectId}/live-agent/conversations/${conversationId}/messages/${messageId}/approve-ticket`,
      null
    );
  }

  rejectTicket(projectId: string, conversationId: string, messageId: string): Observable<ChatMessageDto> {
    return this.http.post<ChatMessageDto>(
      `${this.apiBaseUrl}/projects/${projectId}/live-agent/conversations/${conversationId}/messages/${messageId}/reject-ticket`,
      null
    );
  }
}
