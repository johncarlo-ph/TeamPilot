import { Component, effect, inject, input, output, signal } from '@angular/core';
import { Modal } from '../../../../shared/components/modal/modal';
import { ProjectsService } from '../../../../core/services/projects.service';
import { ProjectDto, USER_ROLES, UserDto, UserRole } from '../../../../core/models';
import { UsersService } from '../../../../core/services/users.service';

@Component({
  selector: 'app-user-edit-modal',
  imports: [Modal],
  templateUrl: './user-edit-modal.html',
})
export class UserEditModal {
  private readonly projectsService = inject(ProjectsService);
  private readonly usersService = inject(UsersService);

  readonly open = input(false);
  readonly user = input<UserDto | null>(null);
  readonly savingRoles = input(false);
  readonly togglingStatus = input(false);
  readonly closed = output<void>();
  readonly rolesChanged = output<UserRole[]>();
  readonly statusToggled = output<void>();

  readonly roles = USER_ROLES;
  readonly allProjects = signal<ProjectDto[]>([]);
  readonly selectedRoles = signal(new Set<UserRole>());
  readonly assignedProjectIds = signal(new Set<string>());
  readonly savingProjects = signal(false);

  constructor() {
    this.projectsService.list().subscribe((projects) => this.allProjects.set(projects));

    effect(() => {
      const user = this.user();
      if (user && this.open()) {
        this.selectedRoles.set(new Set(user.roles));
        this.usersService.getProjects(user.id).subscribe((ids) => {
          this.assignedProjectIds.set(new Set(ids));
        });
      }
    });
  }

  isRoleSelected(role: UserRole): boolean {
    return this.selectedRoles().has(role);
  }

  toggleRole(role: UserRole): void {
    this.selectedRoles.update((current) => {
      const next = new Set(current);
      next.has(role) ? next.delete(role) : next.add(role);
      return next;
    });
  }

  isProjectAssigned(projectId: string): boolean {
    return this.assignedProjectIds().has(projectId);
  }

  toggleProject(projectId: string): void {
    this.assignedProjectIds.update((current) => {
      const next = new Set(current);
      next.has(projectId) ? next.delete(projectId) : next.add(projectId);
      return next;
    });
  }

  saveRoles(): void {
    this.rolesChanged.emit(Array.from(this.selectedRoles()));
  }

  saveProjects(): void {
    const user = this.user();
    if (!user) {
      return;
    }
    this.savingProjects.set(true);
    this.usersService
      .setProjects(user.id, { projectIds: Array.from(this.assignedProjectIds()) })
      .subscribe({
        next: () => this.savingProjects.set(false),
        error: () => this.savingProjects.set(false),
      });
  }
}
