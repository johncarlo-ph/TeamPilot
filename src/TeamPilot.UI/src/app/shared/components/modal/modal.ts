import { Component, input, output } from '@angular/core';

@Component({
  selector: 'app-modal',
  template: `
    @if (open()) {
      <div class="modal d-block" tabindex="-1" role="dialog" (click)="onBackdropClick($event)">
        <div class="modal-dialog" [class.modal-lg]="size() === 'lg'" role="document">
          <div class="modal-content">
            <div class="modal-header">
              <h5 class="modal-title">{{ title() }}</h5>
              <button
                type="button"
                class="btn-close"
                aria-label="Close"
                (click)="closed.emit()"
              ></button>
            </div>
            <div class="modal-body">
              <ng-content></ng-content>
            </div>
          </div>
        </div>
      </div>
      <div class="modal-backdrop show"></div>
    }
  `,
})
export class Modal {
  readonly open = input(false);
  readonly title = input('');
  readonly size = input<'md' | 'lg'>('md');
  readonly closed = output<void>();

  onBackdropClick(event: MouseEvent): void {
    if (event.target === event.currentTarget) {
      this.closed.emit();
    }
  }
}
