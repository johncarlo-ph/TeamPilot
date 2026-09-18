import { CdkDragDrop, DragDropModule } from '@angular/cdk/drag-drop';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Subject, distinctUntilChanged, filter, interval, map, merge, startWith, switchMap } from 'rxjs';
import { TicketsService } from '../../core/services/tickets.service';
import { ReviewsService } from '../../core/services/reviews.service';
import { ProjectsService } from '../../core/services/projects.service';
import { SprintsService } from '../../core/services/sprints.service';
import { ProjectEventsService } from '../../core/services/project-events.service';
import { NotificationService } from '../../core/notification/notification.service';
import { CreateTicketRequest, ProjectDto, ReviewDecision, SprintDto, TicketDto, TicketStatus } from '../../core/models';
import { TicketCard } from './ticket-card/ticket-card';
import { CreateTicketForm } from './create-ticket-form/create-ticket-form';
import { ReviewForm } from '../ticket-detail/review-form/review-form';
import { ChatPanel } from './chat-panel/chat-panel';
import { CancelledTicketsModal } from './cancelled-tickets-modal/cancelled-tickets-modal';

// The SSE stream (see ProjectEventsService) now drives the primary refresh; this is only a
// safety net for a stuck/misbehaving connection, so it's far longer than the old 8s poll.
const SAFETY_POLL_INTERVAL_MS = 60000;

type BoardStatus = Exclude<TicketStatus, 'Cancelled'>;

export const BOARD_COLUMNS: { status: BoardStatus; title: string; icon: string; accentClass: string }[] = [
  { status: 'ToDo', title: 'To Do', icon: '⏳', accentClass: 'board-column--todo' },
  { status: 'InProgress', title: 'In Progress', icon: '🔧', accentClass: 'board-column--inprogress' },
  { status: 'Blocked', title: 'Blocked', icon: '🚫', accentClass: 'board-column--blocked' },
  { status: 'ForReview', title: 'For Review', icon: '👀', accentClass: 'board-column--forreview' },
  { status: 'Done', title: 'Done', icon: '✅', accentClass: 'board-column--done' },
];

// The only drags handleTransition actually acts on - everything else (e.g. dropping a ToDo card
// straight onto Done, or a Blocked card anywhere) is rejected there, so it's kept out of
// connectedIdsFor entirely rather than letting the drag succeed visually just to be bounced back
// with an error notification. cdkDropListConnectedTo is one-directional, so listing a target here
// doesn't also let the reverse drag happen - e.g. ForReview -> Done is listed without Done -> ForReview.
const VALID_DRAG_TARGETS: Record<BoardStatus, BoardStatus[]> = {
  ToDo: ['InProgress'],
  InProgress: ['ForReview'],
  ForReview: ['Done', 'InProgress'],
  Done: [],
  Blocked: [],
};

@Component({
  selector: 'app-board',
  imports: [
    RouterLink,
    DragDropModule,
    TicketCard,
    CreateTicketForm,
    ReviewForm,
    ChatPanel,
    CancelledTicketsModal,
  ],
  templateUrl: './board.html',
})
export class Board {
  private readonly route = inject(ActivatedRoute);
  private readonly ticketsService = inject(TicketsService);
  private readonly reviewsService = inject(ReviewsService);
  private readonly projectsService = inject(ProjectsService);
  private readonly sprintsService = inject(SprintsService);
  private readonly projectEventsService = inject(ProjectEventsService);
  private readonly notifications = inject(NotificationService);

  readonly columns = BOARD_COLUMNS;
  readonly columnIds = BOARD_COLUMNS.map((c) => `col-${c.status}`);

  readonly tickets = signal<TicketDto[]>([]);
  readonly project = signal<ProjectDto | null>(null);
  readonly sprint = signal<SprintDto | null>(null);
  readonly loading = signal(true);

  readonly chatCollapsed = signal(false);

  readonly createFormOpen = signal(false);
  readonly cancelledTicketsOpen = signal(false);
  readonly creatingTicket = signal(false);
  readonly reviewTarget = signal<TicketDto | null>(null);
  readonly reviewInitialDecision = signal<ReviewDecision>('Approve');
  readonly submittingReview = signal(false);

  // Forces the next poll tick to fire immediately after a mutation, cancelling (via switchMap)
  // any poll request that was already in flight before the mutation - without this, a stale
  // in-flight poll response can land after an optimistic update and overwrite it with pre-mutation
  // data, making e.g. a just-created ticket flicker away until the following poll cycle.
  private readonly refreshTrigger$ = new Subject<void>();

  // Ticket id -> status the ticket had before an optimistic transition was applied (see
  // setTicketStatus). The ToDo -> InProgress transition in particular is driven by a detached
  // background task (see OrchestrationService.StartPipelineAsync): the request that kicks it off
  // returns - and the immediate poll tick that follows can complete - before the background task
  // has actually flipped the ticket's status server-side, so that poll can still report the
  // pre-transition status. Reconciling against this map lets the poll merge (see
  // reconcilePendingTransitions) ignore that stale read instead of snapping the card back, while
  // still trusting the server the moment it reports anything other than the pre-transition status.
  private readonly pendingTransitions = new Map<string, TicketStatus>();

  readonly ticketsByStatus = computed(() => {
    const grouped: Record<BoardStatus, TicketDto[]> = {
      ToDo: [],
      InProgress: [],
      Blocked: [],
      ForReview: [],
      Done: [],
    };
    for (const ticket of this.tickets()) {
      const status = ticket.status;
      if (status === 'Cancelled') {
        continue;
      }
      grouped[status].push(ticket);
    }
    return grouped;
  });

  protected get projectId(): string {
    return this.route.snapshot.paramMap.get('projectId')!;
  }

  protected get sprintId(): string {
    return this.route.snapshot.paramMap.get('sprintId')!;
  }

  constructor() {
    this.route.paramMap
      .pipe(
        map((params) => params.get('sprintId')!),
        distinctUntilChanged(),
        switchMap((sprintId) =>
          merge(
            this.projectEventsService.stream(this.projectId).pipe(filter((e) => e.type === 'TicketChanged')),
            interval(SAFETY_POLL_INTERVAL_MS),
            this.refreshTrigger$
          ).pipe(
            startWith(0),
            switchMap(() => this.ticketsService.listForSprint(sprintId))
          )
        ),
        takeUntilDestroyed()
      )
      .subscribe({
        next: (tickets) => {
          this.tickets.set(this.reconcilePendingTransitions(tickets));
          this.loading.set(false);
        },
        error: () => this.loading.set(false),
      });

    this.route.paramMap
      .pipe(
        map((params) => params.get('projectId')!),
        distinctUntilChanged(),
        switchMap((projectId) => this.projectsService.getById(projectId)),
        takeUntilDestroyed()
      )
      .subscribe((project) => this.project.set(project));

    this.route.paramMap
      .pipe(
        map((params) => params.get('sprintId')!),
        distinctUntilChanged(),
        switchMap((sprintId) => this.sprintsService.getById(sprintId)),
        takeUntilDestroyed()
      )
      .subscribe((sprint) => this.sprint.set(sprint));
  }

  columnIdFor(status: TicketStatus): string {
    return `col-${status}`;
  }

  connectedIdsFor(status: BoardStatus): string[] {
    return VALID_DRAG_TARGETS[status].map((target) => this.columnIdFor(target));
  }

  createTicket(request: CreateTicketRequest): void {
    this.creatingTicket.set(true);
    this.ticketsService.create(this.sprintId, request).subscribe({
      next: (ticket) => {
        this.creatingTicket.set(false);
        this.tickets.update((tickets) => [...tickets, ticket]);
        this.refreshTrigger$.next();
        this.notifications.success('Ticket created.');
        this.createFormOpen.set(false);
      },
      error: () => this.creatingTicket.set(false),
    });
  }

  onDrop(event: CdkDragDrop<TicketDto[]>, targetStatus: TicketStatus): void {
    if (event.previousContainer === event.container) {
      return;
    }
    const ticket = event.item.data as TicketDto;
    this.handleTransition(ticket, targetStatus);
  }

  private handleTransition(ticket: TicketDto, targetStatus: TicketStatus): void {
    if (ticket.status === 'ToDo' && targetStatus === 'InProgress') {
      // The pipeline runs detached on the server (see TicketsService.startPipeline), so the
      // POST response comes back before the background run has assigned an agent - it can still
      // report the ticket as ToDo. Move the card locally right away instead of waiting on that
      // response (or the next poll tick) to reflect it, so the drop doesn't visibly bounce the
      // card back to To Do. refreshTrigger$ still forces an immediate poll tick to pick up
      // PipelineRunning and any other server-side fields as soon as they land.
      this.pendingTransitions.set(ticket.id, ticket.status);
      this.setTicketStatus(ticket.id, targetStatus);
      this.ticketsService.startPipeline(ticket.id).subscribe({
        next: (updated) => {
          this.patchTicket({ ...updated, status: targetStatus });
          this.refreshTrigger$.next();
          this.notifications.success('Pipeline started.');
        },
        error: () => {
          this.pendingTransitions.delete(ticket.id);
          this.setTicketStatus(ticket.id, ticket.status);
        },
      });
      return;
    }
    if (ticket.status === 'InProgress' && targetStatus === 'ForReview') {
      this.setTicketStatus(ticket.id, targetStatus);
      this.ticketsService.moveToReview(ticket.id).subscribe({
        next: (updated) => {
          this.patchTicket(updated);
          this.refreshTrigger$.next();
        },
        error: () => this.setTicketStatus(ticket.id, ticket.status),
      });
      return;
    }
    if (ticket.status === 'ForReview' && targetStatus === 'Done') {
      this.reviewInitialDecision.set('Approve');
      this.reviewTarget.set(ticket);
      return;
    }
    if (ticket.status === 'ForReview' && targetStatus === 'InProgress') {
      this.reviewInitialDecision.set('RequestChanges');
      this.reviewTarget.set(ticket);
      return;
    }
    this.notifications.error(`Moving a ticket from ${ticket.status} to ${targetStatus} isn't supported.`);
  }

  moveToBacklog(ticket: TicketDto): void {
    if (!confirm(`Move "${ticket.title}" back to the project's backlog?`)) {
      return;
    }
    this.ticketsService.moveToBacklog(ticket.id).subscribe({
      next: () => {
        this.notifications.success('Ticket moved to the backlog.');
        this.tickets.update((tickets) => tickets.filter((t) => t.id !== ticket.id));
      },
    });
  }

  confirmReview(request: { decision: ReviewDecision; comments: string | null }): void {
    const ticket = this.reviewTarget();
    if (!ticket) {
      return;
    }
    this.submittingReview.set(true);
    this.reviewsService.submit(ticket.id, request).subscribe({
      next: (updated) => {
        this.submittingReview.set(false);
        this.patchTicket(updated);
        this.refreshTrigger$.next();
        this.notifications.success(
          request.decision === 'RequestChanges'
            ? 'Review submitted - the pipeline is now running in the background.'
            : 'Review submitted.'
        );
        this.reviewTarget.set(null);
      },
      error: () => this.submittingReview.set(false),
    });
  }

  private patchTicket(updated: TicketDto): void {
    this.tickets.update((tickets) => tickets.map((t) => (t.id === updated.id ? updated : t)));
  }

  // Guards a polled ticket list against the ToDo -> InProgress race described at
  // pendingTransitions: a ticket still reporting the status it had before the optimistic
  // transition is treated as a stale read and kept at its current (optimistic) status instead of
  // being snapped back. The moment a poll reports anything else for that ticket, the background
  // task has caught up (whether it landed on InProgress, or somewhere else like Blocked on
  // failure) - the pending entry is cleared and the server's value wins from then on.
  private reconcilePendingTransitions(fetched: TicketDto[]): TicketDto[] {
    if (this.pendingTransitions.size === 0) {
      return fetched;
    }
    const current = new Map(this.tickets().map((t) => [t.id, t]));
    return fetched.map((ticket) => {
      const priorStatus = this.pendingTransitions.get(ticket.id);
      if (priorStatus === undefined) {
        return ticket;
      }
      if (ticket.status !== priorStatus) {
        this.pendingTransitions.delete(ticket.id);
        return ticket;
      }
      const optimisticStatus = current.get(ticket.id)?.status;
      return optimisticStatus ? { ...ticket, status: optimisticStatus } : ticket;
    });
  }

  // Moves a ticket to a different board column immediately, ahead of the server confirming it -
  // see the ToDo -> InProgress and InProgress -> ForReview handling in handleTransition, where the
  // drop itself is the user's confirmation (unlike the ForReview drops, which open a review dialog
  // instead of transitioning right away). Also used to revert on a failed request.
  private setTicketStatus(ticketId: string, status: TicketStatus): void {
    this.tickets.update((tickets) =>
      tickets.map((t) => (t.id === ticketId ? { ...t, status } : t))
    );
  }
}
