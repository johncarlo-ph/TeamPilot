import { Component, effect, input, output, signal } from '@angular/core';
import { Modal } from '../../../shared/components/modal/modal';
import { AgentDto } from '../../../core/models';

@Component({
  selector: 'app-assign-agents-modal',
  imports: [Modal],
  templateUrl: './assign-agents-modal.html',
})
export class AssignAgentsModal {
  readonly open = input(false);
  readonly agents = input<AgentDto[]>([]);
  readonly closed = output<void>();
  readonly assigned = output<string[]>();

  readonly selectedIds = signal(new Set<string>());

  constructor() {
    effect(() => {
      if (this.open()) {
        this.selectedIds.set(new Set<string>());
      }
    });
  }

  isSelected(agentId: string): boolean {
    return this.selectedIds().has(agentId);
  }

  toggle(agentId: string): void {
    this.selectedIds.update((current) => {
      const next = new Set(current);
      if (next.has(agentId)) {
        next.delete(agentId);
      } else {
        next.add(agentId);
      }
      return next;
    });
  }

  submit(): void {
    const ids = Array.from(this.selectedIds());
    if (!ids.length) {
      return;
    }
    this.assigned.emit(ids);
  }
}
