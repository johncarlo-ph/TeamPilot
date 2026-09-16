import { DatePipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import {
  Subject,
  distinctUntilChanged,
  filter,
  forkJoin,
  interval,
  map,
  merge,
  startWith,
  switchMap,
  tap,
} from 'rxjs';
import { TicketsService } from '../../core/services/tickets.service';
import { ReviewsService } from '../../core/services/reviews.service';
import { TicketQuestionsService } from '../../core/services/ticket-questions.service';
import { TicketAgentEventsService } from '../../core/services/ticket-agent-events.service';
import { ProjectsService } from '../../core/services/projects.service';
import { ProjectEventsService } from '../../core/services/project-events.service';
import { NotificationService } from '../../core/notification/notification.service';
import {
  ProjectDto,
  ReviewDecision,
  SubmitReviewRequest,
  TicketAgentEventDto,
  TicketDetailDto,
  TicketQuestionDto,
} from '../../core/models';
import { StatusBadge } from '../../shared/components/status-badge/status-badge';
import { CommitDiffViewer } from '../../shared/components/commit-diff-viewer/commit-diff-viewer';
import { BranchPanel } from './branch-panel/branch-panel';
import { ConflictsPanel } from './conflicts-panel/conflicts-panel';
import { ReviewForm } from './review-form/review-form';
import { TicketQuestionPanel } from './ticket-question-panel/ticket-question-panel';
import { AgentEventLogPanel } from './agent-event-log-panel/agent-event-log-panel';
import { buildBranchUrl } from '../../core/utils/git-url.util';

// The SSE stream (see ProjectEventsService) now drives the primary refresh; this is only a
// safety net for a stuck/misbehaving connection, so it's far longer than the old 10s poll.
const SAFETY_POLL_INTERVAL_MS = 60000;
const DESCRIPTION_PREVIEW_LENGTH = 400;

@Component({
  selector: 'app-ticket-detail',
  imports: [
    RouterLink,
    DatePipe,
    StatusBadge,
    CommitDiffViewer,
    BranchPanel,
    ConflictsPanel,
    ReviewForm,
    TicketQuestionPanel,
    AgentEventLogPanel,
  ],
  templateUrl: './ticket-detail.html',
})
export class TicketDetail {
  private readonly route = inject(ActivatedRoute);
  private readonly ticketsService = inject(TicketsService);
  private readonly reviewsService = inject(ReviewsService);
  private readonly ticketQuestionsService = inject(TicketQuestionsService);
  private readonly ticketAgentEventsService = inject(TicketAgentEventsService);
  private readonly projectsService = inject(ProjectsService);
  private readonly projectEventsService = inject(ProjectEventsService);
  private readonly notifications = inject(NotificationService);

  readonly ticket = signal<TicketDetailDto | null>(null);
  readonly questions = signal<TicketQuestionDto[]>([]);
  readonly agentEvents = signal<TicketAgentEventDto[]>([]);
  readonly project = signal<ProjectDto | null>(null);
  readonly loading = signal(true);
  readonly reviewFormOpen = signal(false);
  readonly starting = signal(false);
  readonly cancelling = signal(false);
  readonly submittingReview = signal(false);
  readonly descriptionExpanded = signal(false);

  readonly ticketId = computed(() => this.route.snapshot.paramMap.get('id')!);

  // Forces the next poll tick to fire immediately, cancelling (via switchMap) any poll request
  // already in flight - otherwise a stale in-flight poll can land after refresh() and overwrite
  // its result with pre-mutation data (e.g. the review just submitted appearing to vanish).
  private readonly refreshTrigger$ = new Subject<void>();

  readonly isDescriptionLong = computed(
    () => (this.ticket()?.description?.length ?? 0) > DESCRIPTION_PREVIEW_LENGTH
  );

  readonly descriptionPreview = computed(() => {
    const description = this.ticket()?.description ?? '';
    if (this.descriptionExpanded() || description.length <= DESCRIPTION_PREVIEW_LENGTH) {
      return description;
    }
    return description.slice(0, DESCRIPTION_PREVIEW_LENGTH).trimEnd() + '…';
  });

  readonly sortedReviews = computed(() => {
    const reviews = this.ticket()?.reviews ?? [];
    return [...reviews].sort(
      (a, b) => new Date(b.createdAtUtc).getTime() - new Date(a.createdAtUtc).getTime()
    );
  });

  constructor() {
    this.route.paramMap
      .pipe(
        map((params) => params.get('id')!),
        distinctUntilChanged(),
        tap(() => this.descriptionExpanded.set(false)),
        // A single upfront fetch just to learn the ticket's projectId (needed to open the
        // project-scoped event stream below) - cheap, and simpler than threading projectId in
        // through the route or a separate lookup. The startWith(0) still does its own first
        // real fetch of both ticket and questions immediately after.
        switchMap((id) =>
          this.ticketsService.getById(id).pipe(
            switchMap((initialTicket) =>
              merge(
                this.projectEventsService
                  .stream(initialTicket.projectId)
                  .pipe(filter((e) => e.ticketId === null || e.ticketId === id)),
                interval(SAFETY_POLL_INTERVAL_MS),
                this.refreshTrigger$
              ).pipe(
                startWith(0),
                switchMap(() =>
                  forkJoin({
                    ticket: this.ticketsService.getById(id),
                    questions: this.ticketQuestionsService.listForTicket(id),
                    agentEvents: this.ticketAgentEventsService.listForTicket(id),
                  })
                )
              )
            )
          )
        ),
        takeUntilDestroyed()
      )
      .subscribe({
        next: ({ ticket, questions, agentEvents }) => {
          const isFirstLoad = this.ticket() === null;
          this.ticket.set(ticket);
          this.questions.set(questions);
          this.agentEvents.set(agentEvents);
          this.loading.set(false);
          if (isFirstLoad) {
            this.loadProject(ticket.projectId);
          }
        },
        error: () => this.loading.set(false),
      });
  }

  toggleDescription(): void {
    this.descriptionExpanded.update((expanded) => !expanded);
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
    // Runs detached on the server (see TicketsService.startPipeline) - it can take minutes, so
    // this doesn't wait on it; refresh() re-fetches immediately, and the page's own poll picks
    // up the eventual outcome.
    this.ticketsService.startPipeline(ticket.id).subscribe({
      next: () => {
        this.starting.set(false);
        this.notifications.success('Pipeline started.');
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
        this.notifications.success(
          request.decision === 'RequestChanges'
            ? 'Review submitted - the pipeline is now running in the background.'
            : 'Review submitted.'
        );
        this.reviewFormOpen.set(false);
        this.refresh();
      },
      error: () => {
        this.submittingReview.set(false);
        // Approving can fail because the server just reset one or more conflicts back to
        // Detected (see StaleConflictResolutionException) - refresh so the conflicts panel shows
        // that immediately instead of still displaying them as resolved until something else
        // happens to trigger a refetch.
        this.refresh();
      },
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
    this.refreshTrigger$.next();
  }

  private loadProject(projectId: string): void {
    this.projectsService.getById(projectId).subscribe((project) => this.project.set(project));
  }

  protected readonly reviewInitialDecision: ReviewDecision = 'Approve';
}
