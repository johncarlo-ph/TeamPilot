import { DatePipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { forkJoin, of } from 'rxjs';
import { ProjectsService } from '../../../core/services/projects.service';
import { TicketProjectInstructionDto } from '../../../core/models';

// Read-only: a Research/Design stage's "this referenced project needs a change" note (see
// backend TicketProjectInstruction) is surfaced here for a human to act on manually - it never
// triggers an automated cross-repo write.
@Component({
  selector: 'app-cross-project-instructions-panel',
  imports: [DatePipe],
  templateUrl: './cross-project-instructions-panel.html',
})
export class CrossProjectInstructionsPanel {
  private readonly projectsService = inject(ProjectsService);
  private readonly destroyRef = inject(DestroyRef);

  readonly instructions = input<TicketProjectInstructionDto[]>([]);

  private readonly projectNameById = signal<Record<string, string>>({});

  constructor() {
    effect(() => {
      const ids = [...new Set(this.instructions().map((i) => i.referencedProjectId))].filter(
        (id) => !(id in this.projectNameById())
      );
      if (ids.length === 0) {
        return;
      }
      forkJoin(ids.map((id) => this.projectsService.getById(id)))
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (projects) => {
            this.projectNameById.update((current) => {
              const next = { ...current };
              for (const project of projects) {
                next[project.id] = project.name;
              }
              return next;
            });
          },
          // A referenced project the caller can no longer see (e.g. removed) just falls back to
          // showing its raw id below - not worth failing the whole panel over.
          error: () => of(null),
        });
    });
  }

  projectName(referencedProjectId: string): string {
    return this.projectNameById()[referencedProjectId] ?? referencedProjectId;
  }

  readonly sortedInstructions = computed(() =>
    [...this.instructions()].sort((a, b) => new Date(b.createdAtUtc).getTime() - new Date(a.createdAtUtc).getTime())
  );
}
