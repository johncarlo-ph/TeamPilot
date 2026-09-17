import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';
import { NotificationService } from '../../../core/notification/notification.service';
import { ProjectsService } from '../../../core/services/projects.service';
import { SprintsService } from '../../../core/services/sprints.service';
import {
  CreateSprintRequest,
  ProjectDto,
  SprintDto,
  TicketStatusCountsDto,
  UpdateSprintRequest,
} from '../../../core/models';
import { SprintForm } from '../sprint-form/sprint-form';

// Same 5 board-relevant statuses/icons as BOARD_COLUMNS (features/board/board.ts), but outlined
// rather than solid-filled like app-status-badge's ticket badges - a solid fill (esp. the warning
// yellow) was hard to read at this small badge size, so these use badge-outline-* (see
// styles.scss) instead of text-bg-*. Kept as its own array rather than reusing either component
// directly, since this renders a count, not a ticket's own status label.
const STATUS_SUMMARIES: {
  status: keyof TicketStatusCountsDto;
  title: string;
  icon: string;
  badgeClass: string;
}[] = [
  { status: 'toDo', title: 'To Do', icon: '⏳', badgeClass: 'badge-outline-todo' },
  { status: 'inProgress', title: 'In Progress', icon: '🔧', badgeClass: 'badge-outline-inprogress' },
  { status: 'blocked', title: 'Blocked', icon: '🚫', badgeClass: 'badge-outline-blocked' },
  { status: 'forReview', title: 'For Review', icon: '👀', badgeClass: 'badge-outline-forreview' },
  { status: 'done', title: 'Done', icon: '✅', badgeClass: 'badge-outline-done' },
];

@Component({
  selector: 'app-sprint-list',
  imports: [RouterLink, DatePipe, SprintForm],
  templateUrl: './sprint-list.html',
})
export class SprintList {
  protected readonly authService = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly projectsService = inject(ProjectsService);
  private readonly sprintsService = inject(SprintsService);
  private readonly notifications = inject(NotificationService);

  readonly project = signal<ProjectDto | null>(null);
  readonly sprints = signal<SprintDto[]>([]);
  readonly loading = signal(true);
  readonly formOpen = signal(false);
  readonly editingSprint = signal<SprintDto | null>(null);
  readonly saving = signal(false);
  readonly removingIds = signal<ReadonlySet<string>>(new Set());

  protected get projectId(): string {
    return this.route.snapshot.paramMap.get('projectId')!;
  }

  constructor() {
    this.projectsService.getById(this.projectId).subscribe((project) => this.project.set(project));
    this.reload();
  }

  reload(): void {
    this.loading.set(true);
    this.sprintsService.list(this.projectId).subscribe({
      next: (sprints) => {
        this.sprints.set(sprints);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  openCreate(): void {
    this.editingSprint.set(null);
    this.formOpen.set(true);
  }

  openEdit(sprint: SprintDto): void {
    this.editingSprint.set(sprint);
    this.formOpen.set(true);
  }

  closeForm(): void {
    this.formOpen.set(false);
  }

  isRemoving(sprintId: string): boolean {
    return this.removingIds().has(sprintId);
  }

  /** A sprint can only be removed while none of its tickets are actively running or awaiting
   * approval - mirrors the server-side check in `SprintService.RemoveAsync`, so the button is
   * disabled up front instead of only failing after the click. */
  canRemove(sprint: SprintDto): boolean {
    return sprint.ticketStatusCounts.inProgress === 0 && sprint.ticketStatusCounts.forReview === 0;
  }

  remove(sprint: SprintDto): void {
    if (!confirm(`Remove sprint "${sprint.name}"? Its tickets and history are untouched.`)) {
      return;
    }

    this.removingIds.update((ids) => new Set(ids).add(sprint.id));
    this.sprintsService.remove(sprint.id).subscribe({
      next: () => {
        this.notifications.success('Sprint removed.');
        this.sprints.update((sprints) => sprints.filter((s) => s.id !== sprint.id));
      },
      error: () =>
        this.removingIds.update((ids) => {
          const next = new Set(ids);
          next.delete(sprint.id);
          return next;
        }),
    });
  }

  statusSummaries(sprint: SprintDto) {
    return STATUS_SUMMARIES.map((summary) => ({
      ...summary,
      count: sprint.ticketStatusCounts[summary.status],
    }));
  }

  save(request: CreateSprintRequest | UpdateSprintRequest): void {
    const editing = this.editingSprint();
    const result$ = editing
      ? this.sprintsService.update(editing.id, request as UpdateSprintRequest)
      : this.sprintsService.create(this.projectId, request as CreateSprintRequest);

    this.saving.set(true);
    result$.subscribe({
      next: () => {
        this.saving.set(false);
        this.notifications.success(editing ? 'Sprint updated.' : 'Sprint created.');
        this.formOpen.set(false);
        this.reload();
      },
      error: () => this.saving.set(false),
    });
  }
}
