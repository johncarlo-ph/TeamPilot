import { Component, effect, inject, input, output } from '@angular/core';
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
  readonly closed = output<void>();
  readonly saved = output<CreateProjectRequest | UpdateProjectRequest>();

  readonly form = this.fb.nonNullable.group({
    name: ['', Validators.required],
    description: [''],
    repositoryPath: ['', Validators.required],
  });

  constructor() {
    effect(() => {
      const project = this.project();
      this.form.reset({
        name: project?.name ?? '',
        description: project?.description ?? '',
        repositoryPath: project?.repositoryPath ?? '',
      });
    });
  }

  get title(): string {
    return this.project() ? 'Edit Project' : 'New Project';
  }

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    this.saved.emit({
      name: value.name,
      description: value.description || null,
      repositoryPath: value.repositoryPath,
    });
  }
}
