import { DatePipe } from '@angular/common';
import { Component, computed, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TicketQuestionsService } from '../../../core/services/ticket-questions.service';
import { NotificationService } from '../../../core/notification/notification.service';
import { TicketQuestionDto, TicketStatus } from '../../../core/models';
import { StatusBadge } from '../../../shared/components/status-badge/status-badge';

@Component({
  selector: 'app-ticket-question-panel',
  imports: [FormsModule, DatePipe, StatusBadge],
  templateUrl: './ticket-question-panel.html',
})
export class TicketQuestionPanel {
  private readonly ticketQuestionsService = inject(TicketQuestionsService);
  private readonly notifications = inject(NotificationService);

  readonly ticketId = input.required<string>();
  readonly ticketStatus = input.required<TicketStatus>();
  readonly questions = input<TicketQuestionDto[]>([]);
  readonly changed = output<void>();

  readonly answerText = signal('');
  readonly submitting = signal(false);

  // Only the ticket's own currently-blocking question (if the ticket is still Blocked) gets an
  // action - a question from a prior block-then-resume cycle is history only, even if it was
  // somehow left Pending (shouldn't happen, since answering/retrying always unblocks the ticket).
  readonly pendingQuestion = computed(() => {
    if (this.ticketStatus() !== 'Blocked') {
      return null;
    }
    const questions = this.questions();
    return questions.find((q) => q.status === 'Pending') ?? null;
  });

  onAnswerKeydown(event: Event): void {
    if ((event as KeyboardEvent).shiftKey) {
      return;
    }
    event.preventDefault();
    this.submitAnswer();
  }

  submitAnswer(): void {
    const question = this.pendingQuestion();
    const answer = this.answerText().trim();
    if (!question || !answer || this.submitting()) {
      return;
    }
    this.submitting.set(true);
    this.ticketQuestionsService.answer(this.ticketId(), question.id, { answer }).subscribe({
      next: () => {
        this.submitting.set(false);
        this.answerText.set('');
        this.notifications.success('Answer submitted - pipeline resumed.');
        this.changed.emit();
      },
      error: () => this.submitting.set(false),
    });
  }

  retry(): void {
    this.submitting.set(true);
    this.ticketQuestionsService.retry(this.ticketId()).subscribe({
      next: () => {
        this.submitting.set(false);
        this.notifications.success('Retrying - pipeline resumed.');
        this.changed.emit();
      },
      error: () => this.submitting.set(false),
    });
  }
}
