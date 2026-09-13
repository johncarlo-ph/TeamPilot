import { Component, inject, signal } from '@angular/core';
import { UsersService } from '../../../core/services/users.service';
import { NotificationService } from '../../../core/notification/notification.service';
import { UserDto, UserRole } from '../../../core/models';
import { StatusBadge } from '../../../shared/components/status-badge/status-badge';
import { UserEditModal } from './user-edit-modal/user-edit-modal';

@Component({
  selector: 'app-users',
  imports: [StatusBadge, UserEditModal],
  templateUrl: './users.html',
})
export class Users {
  private readonly usersService = inject(UsersService);
  private readonly notifications = inject(NotificationService);

  readonly users = signal<UserDto[]>([]);
  readonly loading = signal(true);
  readonly editingUser = signal<UserDto | null>(null);
  readonly savingRoles = signal(false);
  readonly togglingStatus = signal(false);

  constructor() {
    this.reload();
  }

  reload(): void {
    this.loading.set(true);
    this.usersService.list().subscribe({
      next: (users) => {
        this.users.set(users);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  edit(user: UserDto): void {
    this.editingUser.set(user);
  }

  saveRoles(roles: UserRole[]): void {
    const user = this.editingUser();
    if (!user) {
      return;
    }
    this.savingRoles.set(true);
    this.usersService.setRoles(user.id, { roles }).subscribe({
      next: (updated) => {
        this.savingRoles.set(false);
        this.patchUser(updated);
        this.editingUser.set(updated);
        this.notifications.success('Roles updated.');
      },
      error: () => this.savingRoles.set(false),
    });
  }

  toggleStatus(): void {
    const user = this.editingUser();
    if (!user) {
      return;
    }
    const status = user.status === 'Active' ? 'Disabled' : 'Active';
    this.togglingStatus.set(true);
    this.usersService.setStatus(user.id, { status }).subscribe({
      next: (updated) => {
        this.togglingStatus.set(false);
        this.patchUser(updated);
        this.editingUser.set(updated);
        this.notifications.success(`Account ${status.toLowerCase()}.`);
      },
      error: () => this.togglingStatus.set(false),
    });
  }

  private patchUser(updated: UserDto): void {
    this.users.update((users) => users.map((u) => (u.id === updated.id ? updated : u)));
  }
}
