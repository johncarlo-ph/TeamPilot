import { DatePipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { distinctUntilChanged, interval, map, startWith, switchMap } from 'rxjs';
import { TicketsService } from '../../core/services/tickets.service';
import { AgentsService } from '../../core/services/agents.service';
import { ReviewsService } from '../../core/services/reviews.service';
import { NotificationService } from '../../core/notification/notification.service';
import { AgentDto, ReviewDecision, SubmitReviewRequest, TicketDetailDto } from '../../core/models';
import { StatusBadge } from '../../shared/components/status-badge/status-badge';
import { DiffViewer } from '../../shared/components/diff-viewer/diff-viewer';
import { BranchPanel } from './branch-panel/branch-panel';
import { ConflictsPanel } from './conflicts-panel/conflicts-panel';
import { ReviewForm } from './review-form/review-form';

const POLL_INTERVAL_MS = 10000;

@Component({
  selector: 'app-ticket-detail',
  imports: [RouterLink, DatePipe, StatusBadge, DiffViewer, BranchPanel, ConflictsPanel, ReviewForm],
  templateUrl: './ticket-detail.html',
})
export class TicketDetail {
  private readonly route = inject(ActivatedRoute);
  private readonly ticketsService = inject(TicketsService);
  private readonly agentsService = inject(AgentsService);
  private readonly reviewsService = inject(ReviewsService);
  private readonly notifications = inject(NotificationService);

  readonly ticket = signal<TicketDetailDto | null>(null);
  readonly loading = signal(true);
  readonly agentsById = signal<Record<string, AgentDto>>({});
  readonly reviewFormOpen = signal(false);

  readonly ticketId = computed(() => this.route.snapshot.paramMap.get('id')!);

  constructor() {
    this.route.paramMap
      .pipe(
        map((params) => params.get('id')!),
        distinctUntilChanged(),
        switchMap((id) =>
          interval(POLL_INTERVAL_MS).pipe(
            startWith(0),
            switchMap(() => this.ticketsService.getById(id))
          )
        ),
        takeUntilDestroyed()
      )
      .subscribe({
        next: (ticket) => {
          const isFirstLoad = this.ticket() === null;
          this.ticket.set(ticket);
          this.loading.set(false);
          if (isFirstLoad) {
            this.loadAgents(ticket.projectId);
          }
        },
        error: () => this.loading.set(false),
      });
  }

  agentName(agentId: string): string {
    return this.agentsById()[agentId]?.name ?? agentId;
  }

  executeAgent(agentId: string): void {
    const ticket = this.ticket();
    if (!ticket) {
      return;
    }
    this.ticketsService.executeAgent(ticket.id, agentId).subscribe({
      next: (result) => {
        this.notifications.success(
          result.commit ? 'Agent produced a new commit.' : 'Agent completed its work.'
        );
        this.refresh();
      },
    });
  }

  submitReview(request: SubmitReviewRequest): void {
    const ticket = this.ticket();
    if (!ticket) {
      return;
    }
    this.reviewsService.submit(ticket.id, request).subscribe({
      next: () => {
        this.notifications.success('Review submitted.');
        this.reviewFormOpen.set(false);
        this.refresh();
      },
    });
  }

  onBranchCreated(): void {
    this.refresh();
  }

  onConflictsChanged(): void {
    this.refresh();
  }

  private refresh(): void {
    this.ticketsService.getById(this.ticketId()).subscribe((ticket) => this.ticket.set(ticket));
  }

  private loadAgents(projectId: string): void {
    this.agentsService.listForProject(projectId).subscribe((agents) => {
      this.agentsById.set(Object.fromEntries(agents.map((a) => [a.id, a])));
    });
  }

  protected readonly reviewInitialDecision: ReviewDecision = 'Approve';
}
