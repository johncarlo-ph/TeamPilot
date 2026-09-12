import { Component, inject } from '@angular/core';
import { NotificationService } from '../../core/notification/notification.service';

@Component({
  selector: 'app-toast-container',
  template: `
    <div
      class="toast-stack position-fixed top-0 start-50 translate-middle-x p-3"
      style="z-index: 1080; width: 100%; max-width: 420px"
    >
      @for (toast of notifications.toasts(); track toast.id) {
        <div class="alert alert-{{ toast.level }} shadow-sm d-flex align-items-center justify-content-between">
          <span>{{ toast.message }}</span>
          <button
            type="button"
            class="btn-close ms-3"
            aria-label="Dismiss"
            (click)="notifications.dismiss(toast.id)"
          ></button>
        </div>
      }
    </div>
  `,
})
export class ToastContainer {
  protected readonly notifications = inject(NotificationService);
}
