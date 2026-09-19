import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { NotificationService } from '../../core/notification/notification.service';
import { ProjectsService } from '../../core/services/projects.service';
import { SprintsService } from '../../core/services/sprints.service';
import { TicketsService } from '../../core/services/tickets.service';
import { CreateTicketRequest, ProjectDto, SprintDto, TicketDto } from '../../core/models';
import { CreateTicketForm } from '../board/create-ticket-form/create-ticket-form';
import { StatusBadge } from '../../shared/components/status-badge/status-badge';
import { MentionText } from '../../shared/components/mention-text/mention-text';

@Component({
  selector: 'app-backlog',
  imports: [RouterLink, CreateTicketForm, StatusBadge, MentionText],
  templateUrl: './backlog.html',
})
export class Backlog {
  private readonly route = inject(ActivatedRoute);
  private readonly projectsService = inject(ProjectsService);
  private readonly sprintsService = inject(SprintsService);
  private readonly ticketsService = inject(TicketsService);
  private readonly notifications = inject(NotificationService);

  readonly project = signal<ProjectDto | null>(null);
  readonly sprints = signal<SprintDto[]>([]);
  readonly tickets = signal<TicketDto[]>([]);
  readonly loading = signal(true);
  readonly formOpen = signal(false);
  readonly creatingTicket = signal(false);
  readonly assigningIds = signal<ReadonlySet<string>>(new Set());

  protected get projectId(): string {
    return this.route.snapshot.paramMap.get('projectId')!;
  }

  constructor() {
    this.projectsService.getById(this.projectId).subscribe((project) => this.project.set(project));
    this.sprintsService.list(this.projectId).subscribe((sprints) => this.sprints.set(sprints));
    this.reload();
  }

  reload(): void {
    this.loading.set(true);
    this.ticketsService.listBacklog(this.projectId).subscribe({
      next: (tickets) => {
        this.tickets.set(tickets);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  createTicket(request: CreateTicketRequest): void {
    this.creatingTicket.set(true);
    this.ticketsService.createBacklog(this.projectId, request).subscribe({
      next: (ticket) => {
        this.creatingTicket.set(false);
        this.tickets.update((tickets) => [ticket, ...tickets]);
        this.notifications.success('Ticket created in backlog.');
        this.formOpen.set(false);
      },
      error: () => this.creatingTicket.set(false),
    });
  }

  isAssigning(ticketId: string): boolean {
    return this.assigningIds().has(ticketId);
  }

  assignToSprint(ticket: TicketDto, sprintId: string): void {
    if (!sprintId || this.isAssigning(ticket.id)) {
      return;
    }

    this.assigningIds.update((ids) => new Set(ids).add(ticket.id));
    this.ticketsService.assignToSprint(ticket.id, { sprintId }).subscribe({
      next: () => {
        this.notifications.success('Ticket assigned to sprint.');
        this.tickets.update((tickets) => tickets.filter((t) => t.id !== ticket.id));
      },
      error: () =>
        this.assigningIds.update((ids) => {
          const next = new Set(ids);
          next.delete(ticket.id);
          return next;
        }),
    });
  }
}
