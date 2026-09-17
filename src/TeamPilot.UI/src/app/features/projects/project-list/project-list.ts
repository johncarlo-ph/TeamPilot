import { Component, DestroyRef, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Subscription, filter } from 'rxjs';
import { AuthService } from '../../../core/services/auth.service';
import { NotificationService } from '../../../core/notification/notification.service';
import { ProjectsService } from '../../../core/services/projects.service';
import { ProjectEventsService } from '../../../core/services/project-events.service';
import {
  CloneProgressPayload,
  CreateProjectRequest,
  ProjectDto,
  UpdateProjectRequest,
} from '../../../core/models';
import { ProjectForm } from '../project-form/project-form';

@Component({
  selector: 'app-project-list',
  imports: [RouterLink, ProjectForm],
  templateUrl: './project-list.html',
})
export class ProjectList {
  protected readonly authService = inject(AuthService);
  private readonly projectsService = inject(ProjectsService);
  private readonly projectEventsService = inject(ProjectEventsService);
  private readonly notifications = inject(NotificationService);
  private readonly destroyRef = inject(DestroyRef);

  readonly projects = signal<ProjectDto[]>([]);
  readonly loading = signal(true);
  readonly formOpen = signal(false);
  readonly editingProject = signal<ProjectDto | null>(null);
  readonly saving = signal(false);
  readonly removingIds = signal<ReadonlySet<string>>(new Set());

  /** Live clone progress by project id, populated only while that project is `Cloning` -
   * `ProjectDto` itself carries no object/byte counts (see `CloneProgressPayload`), so this is
   * kept alongside `projects` rather than folded into it. */
  readonly cloneProgress = signal<Record<string, CloneProgressPayload>>({});

  // One SSE subscription per project currently being watched for clone progress - started the
  // moment a Cloning project appears (see reload/watchCloneProgress) and torn down as soon as it
  // reaches Ready/Failed, or when this component is destroyed, so nothing keeps streaming for a
  // project nobody's watching anymore.
  private readonly cloneProgressSubscriptions = new Map<string, Subscription>();

  constructor() {
    this.reload();
    this.destroyRef.onDestroy(() => {
      for (const subscription of this.cloneProgressSubscriptions.values()) {
        subscription.unsubscribe();
      }
    });
  }

  reload(): void {
    this.loading.set(true);
    this.projectsService.list().subscribe({
      next: (projects) => {
        this.projects.set(projects);
        this.loading.set(false);
        for (const project of projects) {
          if (project.status === 'Cloning') {
            this.watchCloneProgress(project.id);
          }
        }
      },
      error: () => this.loading.set(false),
    });
  }

  /** Fraction (0-100) for a project's progress bar, or `null` while still indeterminate (Git
   * hasn't reported a total object count yet) - see `CloneProgressPayload`. */
  clonePercent(projectId: string): number | null {
    const progress = this.cloneProgress()[projectId];
    if (!progress || progress.totalObjects === 0) {
      return null;
    }
    return Math.min(100, Math.round((progress.receivedObjects / progress.totalObjects) * 100));
  }

  private watchCloneProgress(projectId: string): void {
    if (this.cloneProgressSubscriptions.has(projectId)) {
      return;
    }

    const subscription = this.projectEventsService
      .stream(projectId)
      .pipe(filter((event) => event.type === 'ProjectCloneProgress' && event.cloneProgress !== null))
      .subscribe((event) => {
        const progress = event.cloneProgress!;

        if (progress.status === 'Cloning') {
          this.cloneProgress.update((current) => ({ ...current, [projectId]: progress }));
          return;
        }

        // Final event (Ready/Failed) - stop watching and patch this one project's status/reason
        // in place instead of waiting for the next full reload() to pick it up.
        this.stopWatchingCloneProgress(projectId);
        this.projects.update((projects) =>
          projects.map((project) =>
            project.id === projectId
              ? { ...project, status: progress.status, cloneFailureReason: progress.errorMessage }
              : project
          )
        );

        if (progress.status === 'Ready') {
          this.notifications.success('Project cloned and ready.');
        } else {
          this.notifications.error(progress.errorMessage ?? 'Project clone failed.');
        }
      });

    this.cloneProgressSubscriptions.set(projectId, subscription);
  }

  private stopWatchingCloneProgress(projectId: string): void {
    this.cloneProgressSubscriptions.get(projectId)?.unsubscribe();
    this.cloneProgressSubscriptions.delete(projectId);
    this.cloneProgress.update((current) => {
      const next = { ...current };
      delete next[projectId];
      return next;
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

  isRemoving(projectId: string): boolean {
    return this.removingIds().has(projectId);
  }

  remove(project: ProjectDto): void {
    if (!confirm(`Remove project "${project.name}"? This only hides it - its Git repository and branches are untouched.`)) {
      return;
    }

    this.removingIds.update((ids) => new Set(ids).add(project.id));
    this.projectsService.remove(project.id).subscribe({
      next: () => {
        this.notifications.success('Project removed.');
        this.projects.update((projects) => projects.filter((p) => p.id !== project.id));
      },
      error: () =>
        this.removingIds.update((ids) => {
          const next = new Set(ids);
          next.delete(project.id);
          return next;
        }),
    });
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
        this.notifications.success(editing ? 'Project updated.' : 'Project created - cloning its repository now.');
        this.formOpen.set(false);
        this.reload();
      },
      error: () => this.saving.set(false),
    });
  }
}
