import { DatePipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { distinctUntilChanged, forkJoin, interval, map, startWith, switchMap } from 'rxjs';
import { TicketsService } from '../../core/services/tickets.service';
import { ReviewsService } from '../../core/services/reviews.service';
import { TicketQuestionsService } from '../../core/services/ticket-questions.service';
import { ProjectsService } from '../../core/services/projects.service';
import { NotificationService } from '../../core/notification/notification.service';
import {
  ProjectDto,
  ReviewDecision,
  SubmitReviewRequest,
  TicketDetailDto,
  TicketQuestionDto,
} from '../../core/models';
import { StatusBadge } from '../../shared/components/status-badge/status-badge';
import { DiffViewer } from '../../shared/components/diff-viewer/diff-viewer';
import { BranchPanel } from './branch-panel/branch-panel';
import { ConflictsPanel } from './conflicts-panel/conflicts-panel';
import { ReviewForm } from './review-form/review-form';
import { TicketQuestionPanel } from './ticket-question-panel/ticket-question-panel';
import { buildBranchUrl } from '../../core/utils/git-url.util';

const POLL_INTERVAL_MS = 10000;

@Component({
  selector: 'app-ticket-detail',
  imports: [
    RouterLink,
    DatePipe,
    StatusBadge,
    DiffViewer,
    BranchPanel,
    ConflictsPanel,
    ReviewForm,
    TicketQuestionPanel,
  ],
  templateUrl: './ticket-detail.html',
})
export class TicketDetail {
  private readonly route = inject(ActivatedRoute);
  private readonly ticketsService = inject(TicketsService);
  private readonly reviewsService = inject(ReviewsService);
  private readonly ticketQuestionsService = inject(TicketQuestionsService);
  private readonly projectsService = inject(ProjectsService);
  private readonly notifications = inject(NotificationService);

  readonly ticket = signal<TicketDetailDto | null>(null);
  readonly questions = signal<TicketQuestionDto[]>([]);
  readonly project = signal<ProjectDto | null>(null);
  readonly loading = signal(true);
  readonly reviewFormOpen = signal(false);
  readonly starting = signal(false);
  readonly cancelling = signal(false);
  readonly submittingReview = signal(false);

  readonly ticketId = computed(() => this.route.snapshot.paramMap.get('id')!);

  constructor() {
    this.route.paramMap
      .pipe(
        map((params) => params.get('id')!),
        distinctUntilChanged(),
        switchMap((id) =>
          interval(POLL_INTERVAL_MS).pipe(
            startWith(0),
            switchMap(() =>
              forkJoin({
                ticket: this.ticketsService.getById(id),
                questions: this.ticketQuestionsService.listForTicket(id),
              })
            )
          )
        ),
        takeUntilDestroyed()
      )
      .subscribe({
        next: ({ ticket, questions }) => {
          const isFirstLoad = this.ticket() === null;
          this.ticket.set(ticket);
          this.questions.set(questions);
          this.loading.set(false);
          if (isFirstLoad) {
            this.loadProject(ticket.projectId);
          }
        },
        error: () => this.loading.set(false),
      });
  }

  branchUrl(branchName: string): string | null {
    return buildBranchUrl(this.project()?.remoteUrl, branchName);
  }

  startPipeline(): void {
    const ticket = this.ticket();
    if (!ticket) {
      return;
    }
    const action = ticket.status === 'ToDo' ? 'Start' : 'Run';
    if (!confirm(`${action} the pipeline for "${ticket.title}"?`)) {
      return;
    }
    this.starting.set(true);
    this.ticketsService.startPipeline(ticket.id).subscribe({
      next: (result) => {
        this.starting.set(false);
        this.notifications.success(
          result.testingPassed
            ? 'Pipeline complete - testing passed.'
            : `Pipeline complete - testing failed after ${result.testingAttempts} attempts.`
        );
        this.refresh();
      },
      error: () => this.starting.set(false),
    });
  }

  cancelTicket(): void {
    const ticket = this.ticket();
    if (!ticket || !confirm(`Cancel ticket "${ticket.title}"? This cannot be undone.`)) {
      return;
    }
    const reason = prompt('Reason for cancelling (optional):')?.trim() || null;
    this.cancelling.set(true);
    this.ticketsService.cancel(ticket.id, { reason }).subscribe({
      next: () => {
        this.cancelling.set(false);
        this.notifications.success('Ticket cancelled.');
        this.refresh();
      },
      error: () => this.cancelling.set(false),
    });
  }

  submitReview(request: SubmitReviewRequest): void {
    const ticket = this.ticket();
    if (!ticket) {
      return;
    }
    this.submittingReview.set(true);
    this.reviewsService.submit(ticket.id, request).subscribe({
      next: () => {
        this.submittingReview.set(false);
        this.notifications.success('Review submitted.');
        this.reviewFormOpen.set(false);
        this.refresh();
      },
      error: () => this.submittingReview.set(false),
    });
  }

  onBranchChanged(): void {
    this.refresh();
  }

  onConflictsChanged(): void {
    this.refresh();
  }

  onQuestionsChanged(): void {
    this.refresh();
  }

  private refresh(): void {
    this.ticketsService.getById(this.ticketId()).subscribe((ticket) => this.ticket.set(ticket));
    this.ticketQuestionsService
      .listForTicket(this.ticketId())
      .subscribe((questions) => this.questions.set(questions));
  }

  private loadProject(projectId: string): void {
    this.projectsService.getById(projectId).subscribe((project) => this.project.set(project));
  }

  protected readonly reviewInitialDecision: ReviewDecision = 'Approve';
}
