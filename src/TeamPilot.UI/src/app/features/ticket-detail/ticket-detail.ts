import { DatePipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { distinctUntilChanged, interval, map, startWith, switchMap } from 'rxjs';
import { TicketsService } from '../../core/services/tickets.service';
import { AgentsService } from '../../core/services/agents.service';
import { ReviewsService } from '../../core/services/reviews.service';
import { ProjectsService } from '../../core/services/projects.service';
import { NotificationService } from '../../core/notification/notification.service';
import {
  AgentDto,
  ProjectDto,
  ReviewDecision,
  SubmitReviewRequest,
  TicketDetailDto,
} from '../../core/models';
import { StatusBadge } from '../../shared/components/status-badge/status-badge';
import { DiffViewer } from '../../shared/components/diff-viewer/diff-viewer';
import { BranchPanel } from './branch-panel/branch-panel';
import { ConflictsPanel } from './conflicts-panel/conflicts-panel';
import { ReviewForm } from './review-form/review-form';
import { buildBranchUrl } from '../../core/utils/git-url.util';

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
  private readonly projectsService = inject(ProjectsService);
  private readonly notifications = inject(NotificationService);

  readonly ticket = signal<TicketDetailDto | null>(null);
  readonly project = signal<ProjectDto | null>(null);
  readonly loading = signal(true);
  readonly agents = signal<AgentDto[]>([]);
  readonly agentsById = computed(() =>
    Object.fromEntries(this.agents().map((a) => [a.id, a])) as Record<string, AgentDto>
  );
  readonly reviewFormOpen = signal(false);
  readonly starting = signal(false);

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
            this.loadProject(ticket.projectId);
          }
        },
        error: () => this.loading.set(false),
      });
  }

  agentName(agentId: string): string {
    return this.agentsById()[agentId]?.name ?? agentId;
  }

  branchUrl(branchName: string): string | null {
    return buildBranchUrl(this.project()?.remoteUrl, branchName);
  }

  startPipeline(): void {
    const ticket = this.ticket();
    if (!ticket) {
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
    this.ticketsService.cancel(ticket.id, { reason }).subscribe({
      next: () => {
        this.notifications.success('Ticket cancelled.');
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

  onBranchChanged(): void {
    this.refresh();
  }

  onConflictsChanged(): void {
    this.refresh();
  }

  private refresh(): void {
    this.ticketsService.getById(this.ticketId()).subscribe((ticket) => this.ticket.set(ticket));
  }

  private loadAgents(projectId: string): void {
    this.agentsService.listForProject(projectId).subscribe((agents) => this.agents.set(agents));
  }

  private loadProject(projectId: string): void {
    this.projectsService.getById(projectId).subscribe((project) => this.project.set(project));
  }

  protected readonly reviewInitialDecision: ReviewDecision = 'Approve';
}
