import { CdkDragDrop, DragDropModule } from '@angular/cdk/drag-drop';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Subject, distinctUntilChanged, interval, map, merge, startWith, switchMap } from 'rxjs';
import { TicketsService } from '../../core/services/tickets.service';
import { ReviewsService } from '../../core/services/reviews.service';
import { ProjectsService } from '../../core/services/projects.service';
import { NotificationService } from '../../core/notification/notification.service';
import { ProjectDto, ReviewDecision, TicketDto, TicketStatus } from '../../core/models';
import { TicketCard } from './ticket-card/ticket-card';
import { CreateTicketForm } from './create-ticket-form/create-ticket-form';
import { ReviewForm } from '../ticket-detail/review-form/review-form';
import { ChatPanel } from './chat-panel/chat-panel';

const POLL_INTERVAL_MS = 8000;

type BoardStatus = Exclude<TicketStatus, 'Cancelled'>;

export const BOARD_COLUMNS: { status: BoardStatus; title: string; icon: string; accentClass: string }[] = [
  { status: 'ToDo', title: 'To Do', icon: '⏳', accentClass: 'board-column--todo' },
  { status: 'InProgress', title: 'In Progress', icon: '🔧', accentClass: 'board-column--inprogress' },
  { status: 'Blocked', title: 'Blocked', icon: '🚫', accentClass: 'board-column--blocked' },
  { status: 'ForReview', title: 'For Review', icon: '👀', accentClass: 'board-column--forreview' },
  { status: 'Done', title: 'Done', icon: '✅', accentClass: 'board-column--done' },
];

@Component({
  selector: 'app-board',
  imports: [RouterLink, DragDropModule, TicketCard, CreateTicketForm, ReviewForm, ChatPanel],
  templateUrl: './board.html',
})
export class Board {
  private readonly route = inject(ActivatedRoute);
  private readonly ticketsService = inject(TicketsService);
  private readonly reviewsService = inject(ReviewsService);
  private readonly projectsService = inject(ProjectsService);
  private readonly notifications = inject(NotificationService);

  readonly columns = BOARD_COLUMNS;
  readonly columnIds = BOARD_COLUMNS.map((c) => `col-${c.status}`);

  // Blocked is a dead end for drag-and-drop: entered by the pipeline (a clarifying question or
  // an operational failure), and only ever left via the ticket detail page's answer/retry
  // actions - never by a manual drag. Every other column stays connected to every other.
  private readonly draggableColumnIds = BOARD_COLUMNS.filter((c) => c.status !== 'Blocked').map(
    (c) => `col-${c.status}`
  );

  readonly tickets = signal<TicketDto[]>([]);
  readonly project = signal<ProjectDto | null>(null);
  readonly loading = signal(true);

  readonly chatCollapsed = signal(false);

  readonly createFormOpen = signal(false);
  readonly creatingTicket = signal(false);
  readonly reviewTarget = signal<TicketDto | null>(null);
  readonly reviewInitialDecision = signal<ReviewDecision>('Approve');
  readonly submittingReview = signal(false);

  // Forces the next poll tick to fire immediately after a mutation, cancelling (via switchMap)
  // any poll request that was already in flight before the mutation - without this, a stale
  // in-flight poll response can land after an optimistic update and overwrite it with pre-mutation
  // data, making e.g. a just-created ticket flicker away until the following poll cycle.
  private readonly refreshTrigger$ = new Subject<void>();

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

  constructor() {
    this.route.paramMap
      .pipe(
        map((params) => params.get('projectId')!),
        distinctUntilChanged(),
        switchMap((projectId) =>
          merge(interval(POLL_INTERVAL_MS), this.refreshTrigger$).pipe(
            startWith(0),
            switchMap(() => this.ticketsService.listForProject(projectId))
          )
        ),
        takeUntilDestroyed()
      )
      .subscribe({
        next: (tickets) => {
          this.tickets.set(tickets);
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
  }

  columnIdFor(status: TicketStatus): string {
    return `col-${status}`;
  }

  connectedIdsFor(status: BoardStatus): string[] {
    return status === 'Blocked' ? [] : this.draggableColumnIds;
  }

  createTicket(request: { title: string; description: string | null }): void {
    this.creatingTicket.set(true);
    this.ticketsService.create(this.projectId, request).subscribe({
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
      // The pipeline runs detached on the server (see TicketsService.startPipeline) - it can
      // take minutes, so this doesn't wait on it. refreshTrigger$ forces an immediate poll tick
      // to pick up the status change as soon as it lands; the regular poll covers the rest.
      this.ticketsService.startPipeline(ticket.id).subscribe({
        next: (updated) => {
          this.patchTicket(updated);
          this.refreshTrigger$.next();
          this.notifications.success('Pipeline started.');
        },
      });
      return;
    }
    if (ticket.status === 'InProgress' && targetStatus === 'ForReview') {
      this.ticketsService.moveToReview(ticket.id).subscribe({
        next: (updated) => {
          this.patchTicket(updated);
          this.refreshTrigger$.next();
        },
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

  confirmReview(request: { reviewerName: string; decision: ReviewDecision; comments: string | null }): void {
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
}
