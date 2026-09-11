import { DatePipe } from '@angular/common';
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { InstructionsService } from '../../../core/services/instructions.service';
import { AuthService } from '../../../core/services/auth.service';
import { NotificationService } from '../../../core/notification/notification.service';
import { INSTRUCTION_TYPES, InstructionDto, InstructionType } from '../../../core/models';

@Component({
  selector: 'app-instruction-editor',
  imports: [ReactiveFormsModule, DatePipe],
  templateUrl: './instruction-editor.html',
})
export class InstructionEditor {
  private readonly instructionsService = inject(InstructionsService);
  private readonly authService = inject(AuthService);
  private readonly notifications = inject(NotificationService);
  private readonly fb = inject(FormBuilder);

  readonly agentId = input.required<string>();

  readonly types = INSTRUCTION_TYPES;
  readonly instructions = signal<InstructionDto[]>([]);
  readonly loading = signal(true);

  readonly form = this.fb.nonNullable.group({
    Constitution: [''],
    Guideline: [''],
    Requirement: [''],
  });

  readonly historyByType = computed(() => {
    const grouped: Record<InstructionType, InstructionDto[]> = {
      Constitution: [],
      Guideline: [],
      Requirement: [],
    };
    for (const instruction of this.instructions()) {
      grouped[instruction.type].push(instruction);
    }
    for (const type of this.types) {
      grouped[type].sort((a, b) => b.version - a.version);
    }
    return grouped;
  });

  constructor() {
    effect(() => {
      const id = this.agentId();
      if (id) {
        this.reload(id);
      }
    });
  }

  currentVersionLabel(type: InstructionType): string {
    const current = this.historyByType()[type].find((i) => i.isCurrent);
    return current ? `v${current.version}` : 'none yet';
  }

  save(): void {
    const value = this.form.getRawValue();
    const updatedBy = this.authService.currentUser()?.name ?? null;
    const requests = this.types
      .filter((type) => value[type].trim())
      .map((type) =>
        this.instructionsService.addVersion(this.agentId(), {
          type,
          content: value[type],
          updatedBy,
        })
      );

    if (!requests.length) {
      return;
    }

    let remaining = requests.length;
    requests.forEach((request$) =>
      request$.subscribe({
        next: () => {
          remaining -= 1;
          if (remaining === 0) {
            this.notifications.success('Instructions saved and ready for ingestion.');
            this.reload(this.agentId());
          }
        },
      })
    );
  }

  private reload(agentId: string): void {
    this.loading.set(true);
    this.instructionsService.listForAgent(agentId).subscribe({
      next: (instructions) => {
        this.instructions.set(instructions);
        this.loading.set(false);
        const currentByType: Partial<Record<InstructionType, string>> = {};
        for (const type of this.types) {
          currentByType[type] =
            instructions.find((i) => i.type === type && i.isCurrent)?.content ?? '';
        }
        this.form.reset(currentByType as Record<InstructionType, string>);
      },
      error: () => this.loading.set(false),
    });
  }
}
