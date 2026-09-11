import { CdkDragDrop, DragDropModule } from '@angular/cdk/drag-drop';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { distinctUntilChanged, interval, map, startWith, switchMap } from 'rxjs';
import { AgentsService } from '../../core/services/agents.service';
import { TicketsService } from '../../core/services/tickets.service';
import { ReviewsService } from '../../core/services/reviews.service';
import { NotificationService } from '../../core/notification/notification.service';
import { AgentDto, ReviewDecision, TicketDto, TicketStatus } from '../../core/models';
import { TicketCard } from './ticket-card/ticket-card';
import { CreateTicketForm } from './create-ticket-form/create-ticket-form';
import { AssignAgentsModal } from './assign-agents-modal/assign-agents-modal';
import { ReviewForm } from '../ticket-detail/review-form/review-form';

const POLL_INTERVAL_MS = 8000;

export const BOARD_COLUMNS: { status: TicketStatus; title: string }[] = [
  { status: 'ToDo', title: 'To Do' },
  { status: 'InProgress', title: 'In Progress' },
  { status: 'ForReview', title: 'For Review' },
  { status: 'Done', title: 'Done' },
];

@Component({
  selector: 'app-board',
  imports: [RouterLink, DragDropModule, TicketCard, CreateTicketForm, AssignAgentsModal, ReviewForm],
  templateUrl: './board.html',
})
export class Board {
  private readonly route = inject(ActivatedRoute);
  private readonly ticketsService = inject(TicketsService);
  private readonly agentsService = inject(AgentsService);
  private readonly reviewsService = inject(ReviewsService);
  private readonly notifications = inject(NotificationService);

  readonly columns = BOARD_COLUMNS;
  readonly columnIds = BOARD_COLUMNS.map((c) => `col-${c.status}`);

  readonly tickets = signal<TicketDto[]>([]);
  readonly agents = signal<AgentDto[]>([]);
  readonly loading = signal(true);

  readonly createFormOpen = signal(false);
  readonly assignTarget = signal<TicketDto | null>(null);
  readonly reviewTarget = signal<TicketDto | null>(null);
  readonly reviewInitialDecision = signal<ReviewDecision>('Approve');

  readonly ticketsByStatus = computed(() => {
    const grouped: Record<TicketStatus, TicketDto[]> = {
      ToDo: [],
      InProgress: [],
      ForReview: [],
      Done: [],
    };
    for (const ticket of this.tickets()) {
      grouped[ticket.status].push(ticket);
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
        switchMap((projectId) => this.agentsService.listForProject(projectId)),
        takeUntilDestroyed()
      )
      .subscribe((agents) => this.agents.set(agents));
  }

  columnIdFor(status: TicketStatus): string {
    return `col-${status}`;
  }

  createTicket(request: { title: string; description: string | null }): void {
    this.ticketsService.create(this.projectId, request).subscribe({
      next: (ticket) => {
        this.tickets.update((tickets) => [...tickets, ticket]);
        this.notifications.success('Ticket created.');
        this.createFormOpen.set(false);
      },
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
      this.assignTarget.set(ticket);
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

  confirmAssignAgents(agentIds: string[]): void {
    const ticket = this.assignTarget();
    if (!ticket) {
      return;
    }
    this.ticketsService.assignAgents(ticket.id, { agentIds }).subscribe({
      next: (updated) => {
        this.patchTicket(updated);
        this.notifications.success('Agents assigned.');
        this.assignTarget.set(null);
      },
    });
  }

  confirmReview(request: { reviewerName: string; decision: ReviewDecision; comments: string | null }): void {
    const ticket = this.reviewTarget();
    if (!ticket) {
      return;
    }
    this.reviewsService.submit(ticket.id, request).subscribe({
      next: (updated) => {
        this.patchTicket(updated);
        this.notifications.success('Review submitted.');
        this.reviewTarget.set(null);
      },
    });
  }

  private patchTicket(updated: TicketDto): void {
    this.tickets.update((tickets) => tickets.map((t) => (t.id === updated.id ? updated : t)));
  }
}
