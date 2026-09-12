import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { InstructionTemplatesService } from '../../../core/services/instruction-templates.service';
import { NotificationService } from '../../../core/notification/notification.service';
import { InstructionTemplateDto } from '../../../core/models';
import {
  InstructionTemplateForm,
  InstructionTemplateFormValue,
} from './instruction-template-form/instruction-template-form';

@Component({
  selector: 'app-instruction-templates',
  imports: [DatePipe, InstructionTemplateForm],
  templateUrl: './instruction-templates.html',
})
export class InstructionTemplates {
  private readonly templatesService = inject(InstructionTemplatesService);
  private readonly notifications = inject(NotificationService);

  readonly templates = signal<InstructionTemplateDto[]>([]);
  readonly loading = signal(true);
  readonly formOpen = signal(false);
  readonly editingTemplate = signal<InstructionTemplateDto | null>(null);

  constructor() {
    this.reload();
  }

  reload(): void {
    this.loading.set(true);
    this.templatesService.list().subscribe({
      next: (templates) => {
        this.templates.set(templates);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  openCreate(): void {
    this.editingTemplate.set(null);
    this.formOpen.set(true);
  }

  openEdit(template: InstructionTemplateDto): void {
    this.editingTemplate.set(template);
    this.formOpen.set(true);
  }

  save(value: InstructionTemplateFormValue): void {
    const editing = this.editingTemplate();
    const request$ = editing
      ? this.templatesService.update(editing.id, { name: value.name, content: value.content })
      : this.templatesService.create(value);

    request$.subscribe({
      next: () => {
        this.notifications.success(editing ? 'Template updated.' : 'Template created.');
        this.formOpen.set(false);
        this.reload();
      },
    });
  }

  delete(template: InstructionTemplateDto): void {
    if (!confirm(`Delete instruction template "${template.name}"? This cannot be undone.`)) {
      return;
    }
    this.templatesService.delete(template.id).subscribe({
      next: () => {
        this.notifications.success('Template deleted.');
        this.templates.update((templates) => templates.filter((t) => t.id !== template.id));
      },
    });
  }
}
