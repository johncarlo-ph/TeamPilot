import { Component, effect, inject, input, output } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Modal } from '../../../shared/components/modal/modal';
import { AGENT_ROLES, AgentRole, CreateAgentRequest } from '../../../core/models';

@Component({
  selector: 'app-agent-form',
  imports: [ReactiveFormsModule, Modal],
  templateUrl: './agent-form.html',
})
export class AgentForm {
  private readonly fb = inject(FormBuilder);

  readonly open = input(false);
  readonly closed = output<void>();
  readonly created = output<CreateAgentRequest>();

  readonly roles = AGENT_ROLES;

  readonly form = this.fb.nonNullable.group({
    name: ['', Validators.required],
    role: this.fb.nonNullable.control<AgentRole>('Coding'),
    configurationJson: [''],
  });

  constructor() {
    effect(() => {
      if (this.open()) {
        this.form.reset({ name: '', role: 'Coding', configurationJson: '' });
      }
    });
  }

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    this.created.emit({
      name: value.name,
      role: value.role,
      configurationJson: value.configurationJson || null,
    });
  }
}
