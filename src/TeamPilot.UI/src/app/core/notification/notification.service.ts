import { Injectable, signal } from '@angular/core';

export type NotificationLevel = 'success' | 'danger' | 'warning' | 'info';

export interface NotificationToast {
  id: number;
  level: NotificationLevel;
  message: string;
}

let nextId = 1;

@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly toastsSignal = signal<NotificationToast[]>([]);
  readonly toasts = this.toastsSignal.asReadonly();

  show(message: string, level: NotificationLevel = 'info', durationMs = 5000): void {
    const toast: NotificationToast = { id: nextId++, level, message };
    this.toastsSignal.update((toasts) => [...toasts, toast]);
    setTimeout(() => this.dismiss(toast.id), durationMs);
  }

  success(message: string): void {
    this.show(message, 'success');
  }

  error(message: string): void {
    this.show(message, 'danger', 8000);
  }

  dismiss(id: number): void {
    this.toastsSignal.update((toasts) => toasts.filter((t) => t.id !== id));
  }
}
