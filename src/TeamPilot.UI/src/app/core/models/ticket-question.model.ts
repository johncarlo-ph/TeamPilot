import { TicketQuestionKind, TicketQuestionStatus } from './enums';

export interface TicketQuestionDto {
  id: string;
  ticketId: string;
  agentId: string | null;
  kind: TicketQuestionKind;
  prompt: string;
  status: TicketQuestionStatus;
  answerText: string | null;
  answeredBy: string | null;
  answeredAtUtc: string | null;
  createdAtUtc: string;
}

export interface AnswerTicketQuestionRequest {
  answer: string;
}
