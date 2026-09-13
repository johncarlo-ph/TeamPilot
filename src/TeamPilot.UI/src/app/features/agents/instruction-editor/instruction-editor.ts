import { DatePipe } from '@angular/common';
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { InstructionsService } from '../../../core/services/instructions.service';
import { InstructionTemplatesService } from '../../../core/services/instruction-templates.service';
import { AuthService } from '../../../core/services/auth.service';
import { NotificationService } from '../../../core/notification/notification.service';
import {
  AgentRole,
  INSTRUCTION_TYPES,
  InstructionDto,
  InstructionTemplateDto,
  InstructionType,
} from '../../../core/models';

@Component({
  selector: 'app-instruction-editor',
  imports: [ReactiveFormsModule, DatePipe],
  templateUrl: './instruction-editor.html',
})
export class InstructionEditor {
  private readonly instructionsService = inject(InstructionsService);
  private readonly instructionTemplatesService = inject(InstructionTemplatesService);
  private readonly authService = inject(AuthService);
  private readonly notifications = inject(NotificationService);
  private readonly fb = inject(FormBuilder);

  readonly agentId = input.required<string>();
  readonly agentRole = input.required<AgentRole>();

  readonly types = INSTRUCTION_TYPES;
  readonly instructions = signal<InstructionDto[]>([]);
  readonly templates = signal<InstructionTemplateDto[]>([]);
  readonly loading = signal(true);
  readonly saving = signal(false);

  readonly form = this.fb.nonNullable.group({
    Constitution: [''],
    Guideline: [''],
    Requirement: [''],
  });

  readonly selectedTemplateIds = signal<Record<InstructionType, string>>({
    Constitution: '',
    Guideline: '',
    Requirement: '',
  });

  readonly isCustomAgent = computed(() => this.agentRole() === 'Custom');

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

  readonly templatesByType = computed(() => {
    const grouped: Record<InstructionType, InstructionTemplateDto[]> = {
      Constitution: [],
      Guideline: [],
      Requirement: [],
    };
    for (const template of this.templates()) {
      grouped[template.type].push(template);
    }
    return grouped;
  });

  constructor() {
    effect(() => {
      const id = this.agentId();
      const role = this.agentRole();
      if (id) {
        this.reload(id);
      }
      if (role) {
        this.instructionTemplatesService.list(role).subscribe((templates) => this.templates.set(templates));
      }
    });

    effect(() => {
      if (this.isCustomAgent()) {
        this.form.controls.Constitution.enable({ emitEvent: false });
      } else {
        this.form.controls.Constitution.disable({ emitEvent: false });
      }
    });
  }

  currentVersionLabel(type: InstructionType): string {
    const current = this.historyByType()[type].find((i) => i.isCurrent);
    return current ? `v${current.version}` : 'none yet';
  }

  applyTemplate(type: InstructionType, templateId: string): void {
    if (type === 'Constitution' && !this.isCustomAgent()) {
      return;
    }
    const template = this.templatesByType()[type].find((t) => t.id === templateId);
    if (template) {
      this.form.controls[type].setValue(template.content);
    }
    this.selectedTemplateIds.update((ids) => ({ ...ids, [type]: templateId }));
  }

  save(): void {
    const value = this.form.getRawValue();
    const updatedBy = this.authService.currentUser()?.name ?? null;
    const requests = this.types
      .filter((type) => type !== 'Constitution' || this.isCustomAgent())
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

    this.saving.set(true);
    let remaining = requests.length;
    requests.forEach((request$) =>
      request$.subscribe({
        next: () => {
          remaining -= 1;
          if (remaining === 0) {
            this.saving.set(false);
            this.notifications.success('Instructions saved and ready for ingestion.');
            this.reload(this.agentId());
          }
        },
        error: () => this.saving.set(false),
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
        this.selectedTemplateIds.set({ Constitution: '', Guideline: '', Requirement: '' });
      },
      error: () => this.loading.set(false),
    });
  }
}
