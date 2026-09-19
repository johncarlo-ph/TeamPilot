import { Component, effect, inject, input, output } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Modal } from '../../../shared/components/modal/modal';
import { MentionEditor } from '../../../shared/components/mention-editor/mention-editor';
import { CreateTicketRequest } from '../../../core/models';

@Component({
  selector: 'app-create-ticket-form',
  imports: [ReactiveFormsModule, Modal, MentionEditor],
  templateUrl: './create-ticket-form.html',
})
export class CreateTicketForm {
  private readonly fb = inject(FormBuilder);

  readonly open = input(false);
  readonly creating = input(false);
  readonly projectId = input.required<string>();
  readonly projectName = input.required<string>();
  readonly closed = output<void>();
  readonly created = output<CreateTicketRequest>();

  readonly form = this.fb.nonNullable.group({
    title: ['', Validators.required],
    description: [''],
    acceptanceCriteria: ['', Validators.required],
  });

  constructor() {
    effect(() => {
      if (this.open()) {
        this.form.reset({ title: '', description: '', acceptanceCriteria: '' });
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
      title: value.title,
      description: value.description || null,
      acceptanceCriteria: value.acceptanceCriteria,
    });
  }
}
