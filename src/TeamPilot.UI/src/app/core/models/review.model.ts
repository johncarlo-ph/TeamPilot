import { ReviewDecision } from './enums';

export interface ReviewDto {
  id: string;
  ticketId: string;
  reviewerName: string;
  decision: ReviewDecision;
  comments: string | null;
  createdAtUtc: string;
}

export interface SubmitReviewRequest {
  decision: ReviewDecision;
  comments: string | null;
}
