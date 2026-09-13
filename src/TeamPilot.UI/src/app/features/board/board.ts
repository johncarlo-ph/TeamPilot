import { CdkDragDrop, DragDropModule } from '@angular/cdk/drag-drop';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { distinctUntilChanged, interval, map, startWith, switchMap } from 'rxjs';
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

  readonly tickets = signal<TicketDto[]>([]);
  readonly project = signal<ProjectDto | null>(null);
  readonly loading = signal(true);

  readonly createFormOpen = signal(false);
  readonly creatingTicket = signal(false);
  readonly reviewTarget = signal<TicketDto | null>(null);
  readonly reviewInitialDecision = signal<ReviewDecision>('Approve');
  readonly submittingReview = signal(false);

  readonly ticketsByStatus = computed(() => {
    const grouped: Record<BoardStatus, TicketDto[]> = {
      ToDo: [],
      InProgress: [],
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
          interval(POLL_INTERVAL_MS).pipe(
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

  createTicket(request: { title: string; description: string | null }): void {
    this.creatingTicket.set(true);
    this.ticketsService.create(this.projectId, request).subscribe({
      next: (ticket) => {
        this.creatingTicket.set(false);
        this.tickets.update((tickets) => [...tickets, ticket]);
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
      this.ticketsService.startPipeline(ticket.id).subscribe({
        next: (result) => {
          this.patchTicket(result.ticket);
          this.notifications.success(
            result.testingPassed
              ? 'Pipeline complete - testing passed.'
              : `Pipeline complete - testing failed after ${result.testingAttempts} attempts.`
          );
        },
      });
      return;
    }
    if (ticket.status === 'InProgress' && targetStatus === 'ForReview') {
      this.ticketsService.moveToReview(ticket.id).subscribe({
        next: (updated) => this.patchTicket(updated),
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
        this.notifications.success('Review submitted.');
        this.reviewTarget.set(null);
      },
      error: () => this.submittingReview.set(false),
    });
  }

  private patchTicket(updated: TicketDto): void {
    this.tickets.update((tickets) => tickets.map((t) => (t.id === updated.id ? updated : t)));
  }
}
