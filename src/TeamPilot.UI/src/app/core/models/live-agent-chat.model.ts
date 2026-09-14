import { ChatMessageRole } from './enums';

export interface ConversationDto {
  id: string;
  projectId: string;
  title: string;
  createdByUserId: string | null;
  createdByName: string | null;
  createdAtUtc: string;
  updatedAtUtc: string | null;
}

export interface CreateConversationRequest {
  title: string | null;
}

export interface RenameConversationRequest {
  title: string;
}

export interface ChatMessageDto {
  id: string;
  role: ChatMessageRole;
  content: string;
  proposedTicketTitle: string | null;
  proposedTicketDescription: string | null;
  createdTicketId: string | null;
  ticketRejected: boolean;
  senderName: string | null;
  createdAtUtc: string;
}

export interface SendChatMessageRequest {
  content: string;
}
