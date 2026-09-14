import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { TicketQuestionPanel } from './ticket-question-panel';
import { TicketQuestionsService } from '../../../core/services/ticket-questions.service';
import { NotificationService } from '../../../core/notification/notification.service';
import { TicketDto, TicketQuestionDto } from '../../../core/models';

function question(overrides: Partial<TicketQuestionDto> = {}): TicketQuestionDto {
  return {
    id: 'question-1',
    ticketId: 'ticket-1',
    agentId: null,
    kind: 'Question',
    prompt: 'Which endpoint should this call?',
    status: 'Pending',
    answerText: null,
    answeredBy: null,
    answeredAtUtc: null,
    createdAtUtc: new Date().toISOString(),
    ...overrides,
  };
}

function enterEvent(shiftKey: boolean): KeyboardEvent {
  return { shiftKey, preventDefault: vi.fn() } as unknown as KeyboardEvent;
}

describe('TicketQuestionPanel', () => {
  let fixture: ComponentFixture<TicketQuestionPanel>;
  let ticketQuestionsService: {
    answer: ReturnType<typeof vi.fn>;
    retry: ReturnType<typeof vi.fn>;
  };

  function setUp(questions: TicketQuestionDto[]): void {
    ticketQuestionsService = {
      answer: vi.fn().mockReturnValue(of({} as TicketDto)),
      retry: vi.fn().mockReturnValue(of({} as TicketDto)),
    };

    TestBed.configureTestingModule({
      imports: [TicketQuestionPanel],
      providers: [
        { provide: TicketQuestionsService, useValue: ticketQuestionsService },
        { provide: NotificationService, useValue: { success: vi.fn(), error: vi.fn() } },
      ],
    });

    fixture = TestBed.createComponent(TicketQuestionPanel);
    fixture.componentRef.setInput('ticketId', 'ticket-1');
    fixture.componentRef.setInput('ticketStatus', 'Blocked');
    fixture.componentRef.setInput('questions', questions);
    fixture.detectChanges();
  }

  it('onAnswerKeydown_PlainEnterWithAnswerTyped_SubmitsAndPreventsDefault', () => {
    setUp([question()]);
    fixture.componentInstance.answerText.set('Use /api/widgets');
    const event = enterEvent(false);

    fixture.componentInstance.onAnswerKeydown(event);

    expect(event.preventDefault).toHaveBeenCalled();
    expect(ticketQuestionsService.answer).toHaveBeenCalledWith('ticket-1', 'question-1', {
      answer: 'Use /api/widgets',
    });
  });

  it('onAnswerKeydown_ShiftEnter_DoesNotSubmitOrPreventDefault', () => {
    setUp([question()]);
    fixture.componentInstance.answerText.set('Use /api/widgets');
    const event = enterEvent(true);

    fixture.componentInstance.onAnswerKeydown(event);

    expect(event.preventDefault).not.toHaveBeenCalled();
    expect(ticketQuestionsService.answer).not.toHaveBeenCalled();
  });

  it('submitAnswer_AlreadySubmitting_DoesNotSendASecondRequest', () => {
    setUp([question()]);
    fixture.componentInstance.answerText.set('Use /api/widgets');
    fixture.componentInstance.submitting.set(true);

    fixture.componentInstance.submitAnswer();

    expect(ticketQuestionsService.answer).not.toHaveBeenCalled();
  });
});
