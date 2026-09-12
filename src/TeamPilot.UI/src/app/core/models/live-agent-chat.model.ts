import { ChatMessageRole } from './enums';

export interface ChatMessageDto {
  id: string;
  role: ChatMessageRole;
  content: string;
  proposedTicketTitle: string | null;
  proposedTicketDescription: string | null;
  createdAtUtc: string;
}

export interface SendChatMessageRequest {
  content: string;
}
