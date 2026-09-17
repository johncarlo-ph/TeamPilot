import { Component, computed, effect, inject, input, output } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Modal } from '../../../shared/components/modal/modal';
import { CreateSprintRequest, SprintDto, UpdateSprintRequest } from '../../../core/models';

@Component({
  selector: 'app-sprint-form',
  imports: [ReactiveFormsModule, Modal],
  templateUrl: './sprint-form.html',
})
export class SprintForm {
  private readonly fb = inject(FormBuilder);

  readonly open = input(false);
  readonly sprint = input<SprintDto | null>(null);
  readonly saving = input(false);
  readonly closed = output<void>();
  readonly saved = output<CreateSprintRequest | UpdateSprintRequest>();

  readonly isEditing = computed(() => this.sprint() !== null);

  readonly form = this.fb.nonNullable.group({
    name: ['', Validators.required],
    baseBranch: ['main', Validators.required],
    sprintStartDate: [''],
    sprintEndDate: [''],
    sprintGoal: [''],
  });

  constructor() {
    effect(() => {
      const sprint = this.sprint();
      this.form.reset({
        name: sprint?.name ?? '',
        baseBranch: sprint?.baseBranch ?? 'main',
        sprintStartDate: sprint?.sprintStartDate?.substring(0, 10) ?? '',
        sprintEndDate: sprint?.sprintEndDate?.substring(0, 10) ?? '',
        sprintGoal: sprint?.sprintGoal ?? '',
      });
    });
  }

  get title(): string {
    return this.isEditing() ? 'Edit Sprint' : 'New Sprint';
  }

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();

    if (this.isEditing()) {
      this.saved.emit({
        name: value.name,
        baseBranch: value.baseBranch,
        sprintStartDate: value.sprintStartDate || null,
        sprintEndDate: value.sprintEndDate || null,
        sprintGoal: value.sprintGoal || null,
      } satisfies UpdateSprintRequest);
      return;
    }

    this.saved.emit({
      name: value.name,
      baseBranch: value.baseBranch || null,
      sprintStartDate: value.sprintStartDate || null,
      sprintEndDate: value.sprintEndDate || null,
      sprintGoal: value.sprintGoal || null,
    } satisfies CreateSprintRequest);
  }
}
