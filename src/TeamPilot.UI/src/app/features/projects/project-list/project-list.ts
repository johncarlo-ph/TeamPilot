import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';
import { NotificationService } from '../../../core/notification/notification.service';
import { ProjectsService } from '../../../core/services/projects.service';
import { CreateProjectRequest, ProjectDto, UpdateProjectRequest } from '../../../core/models';
import { ProjectForm } from '../project-form/project-form';

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
