import { Component, effect, inject, input, output } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Modal } from '../../../../shared/components/modal/modal';
import {
  AGENT_ROLES,
  AgentRole,
  INSTRUCTION_TYPES,
  InstructionTemplateDto,
  InstructionType,
} from '../../../../core/models';

export interface InstructionTemplateFormValue {
  name: string;
  role: AgentRole;
  type: InstructionType;
  content: string;
}

@Component({
  selector: 'app-instruction-template-form',
  imports: [ReactiveFormsModule, Modal],
  templateUrl: './instruction-template-form.html',
})
export class InstructionTemplateForm {
  private readonly fb = inject(FormBuilder);

  readonly open = input(false);
  readonly template = input<InstructionTemplateDto | null>(null);
  readonly closed = output<void>();
  readonly saved = output<InstructionTemplateFormValue>();

  readonly roles = AGENT_ROLES;
  readonly types = INSTRUCTION_TYPES;

  readonly form = this.fb.nonNullable.group({
    name: ['', Validators.required],
    role: this.fb.nonNullable.control<AgentRole>('Research'),
    type: this.fb.nonNullable.control<InstructionType>('Constitution'),
    content: ['', Validators.required],
  });

  get isEditing(): boolean {
    return this.template() !== null;
  }

  constructor() {
    effect(() => {
      if (!this.open()) {
        return;
      }
      const template = this.template();
      this.form.reset(
        template
          ? { name: template.name, role: template.role, type: template.type, content: template.content }
          : { name: '', role: 'Research', type: 'Constitution', content: '' }
      );
      if (template) {
        this.form.controls.role.disable();
        this.form.controls.type.disable();
      } else {
        this.form.controls.role.enable();
        this.form.controls.type.enable();
      }
    });
  }

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.saved.emit(this.form.getRawValue());
  }
}
