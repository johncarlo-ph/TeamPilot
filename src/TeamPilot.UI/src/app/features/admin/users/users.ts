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
    this.usersService.setRoles(user.id, { roles }).subscribe({
      next: (updated) => {
        this.patchUser(updated);
        this.editingUser.set(updated);
        this.notifications.success('Roles updated.');
      },
    });
  }

  toggleStatus(): void {
    const user = this.editingUser();
    if (!user) {
      return;
    }
    const status = user.status === 'Active' ? 'Disabled' : 'Active';
    this.usersService.setStatus(user.id, { status }).subscribe({
      next: (updated) => {
        this.patchUser(updated);
        this.editingUser.set(updated);
        this.notifications.success(`Account ${status.toLowerCase()}.`);
      },
    });
  }

  private patchUser(updated: UserDto): void {
    this.users.update((users) => users.map((u) => (u.id === updated.id ? updated : u)));
  }
}
