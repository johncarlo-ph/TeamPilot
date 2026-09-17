import { Component, computed, effect, inject, input, output } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Modal } from '../../../shared/components/modal/modal';
import { CreateProjectRequest, ProjectDto, UpdateProjectRequest } from '../../../core/models';

@Component({
  selector: 'app-project-form',
  imports: [ReactiveFormsModule, Modal],
  templateUrl: './project-form.html',
})
export class ProjectForm {
  private readonly fb = inject(FormBuilder);

  readonly open = input(false);
  readonly project = input<ProjectDto | null>(null);
  readonly saving = input(false);
  readonly closed = output<void>();
  readonly saved = output<CreateProjectRequest | UpdateProjectRequest>();

  readonly isEditing = computed(() => this.project() !== null);

  readonly form = this.fb.nonNullable.group({
    name: ['', Validators.required],
    description: [''],
    remoteUrl: ['', [Validators.required, Validators.pattern(/^https:\/\/\S+/)]],
    accessToken: ['', Validators.required],
    baseBranch: ['main', Validators.required],
    sprintStartDate: [''],
    sprintEndDate: [''],
    sprintGoal: [''],
  });

  constructor() {
    effect(() => {
      const project = this.project();
      this.form.reset({
        name: project?.name ?? '',
        description: project?.description ?? '',
        remoteUrl: project?.remoteUrl ?? '',
        accessToken: '',
        baseBranch: project?.baseBranch ?? 'main',
        sprintStartDate: project?.sprintStartDate?.substring(0, 10) ?? '',
        sprintEndDate: project?.sprintEndDate?.substring(0, 10) ?? '',
        sprintGoal: project?.sprintGoal ?? '',
      });

      // Access token is required to create a project, but optional on edit (blank = keep the
      // currently stored token).
      const accessToken = this.form.controls.accessToken;
      if (project) {
        accessToken.clearValidators();
      } else {
        accessToken.setValidators(Validators.required);
      }
      accessToken.updateValueAndValidity();
    });
  }

  get title(): string {
    return this.isEditing() ? 'Edit Project' : 'New Project';
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
        description: value.description || null,
        accessToken: value.accessToken || null,
        baseBranch: value.baseBranch,
        sprintStartDate: value.sprintStartDate || null,
        sprintEndDate: value.sprintEndDate || null,
        sprintGoal: value.sprintGoal || null,
      } satisfies UpdateProjectRequest);
      return;
    }

    this.saved.emit({
      name: value.name,
      description: value.description || null,
      remoteUrl: value.remoteUrl,
      accessToken: value.accessToken,
      baseBranch: value.baseBranch || null,
      sprintStartDate: value.sprintStartDate || null,
      sprintEndDate: value.sprintEndDate || null,
      sprintGoal: value.sprintGoal || null,
    } satisfies CreateProjectRequest);
  }
}
