import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';
import { NotificationService } from '../../../core/notification/notification.service';
import { ProjectsService } from '../../../core/services/projects.service';
import {
  CreateProjectRequest,
  ProjectDto,
  TicketStatusCountsDto,
  UpdateProjectRequest,
} from '../../../core/models';
import { ProjectForm } from '../project-form/project-form';

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
  selector: 'app-project-list',
  imports: [RouterLink, ProjectForm],
  templateUrl: './project-list.html',
})
export class ProjectList {
  protected readonly authService = inject(AuthService);
  private readonly projectsService = inject(ProjectsService);
  private readonly notifications = inject(NotificationService);

  readonly projects = signal<ProjectDto[]>([]);
  readonly loading = signal(true);
  readonly formOpen = signal(false);
  readonly editingProject = signal<ProjectDto | null>(null);
  readonly saving = signal(false);

  constructor() {
    this.reload();
  }

  reload(): void {
    this.loading.set(true);
    this.projectsService.list().subscribe({
      next: (projects) => {
        this.projects.set(projects);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  openCreate(): void {
    this.editingProject.set(null);
    this.formOpen.set(true);
  }

  openEdit(project: ProjectDto): void {
    this.editingProject.set(project);
    this.formOpen.set(true);
  }

  closeForm(): void {
    this.formOpen.set(false);
  }

  statusSummaries(project: ProjectDto) {
    return STATUS_SUMMARIES.map((summary) => ({
      ...summary,
      count: project.ticketStatusCounts[summary.status],
    }));
  }

  save(request: CreateProjectRequest | UpdateProjectRequest): void {
    const editing = this.editingProject();
    const result$ = editing
      ? this.projectsService.update(editing.id, request as UpdateProjectRequest)
      : this.projectsService.create(request as CreateProjectRequest);

    this.saving.set(true);
    result$.subscribe({
      next: () => {
        this.saving.set(false);
        this.notifications.success(editing ? 'Project updated.' : 'Project created.');
        this.formOpen.set(false);
        this.reload();
      },
      error: () => this.saving.set(false),
    });
  }
}
