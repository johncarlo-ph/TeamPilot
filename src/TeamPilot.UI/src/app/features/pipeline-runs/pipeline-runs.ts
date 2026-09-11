import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { PipelineRunsService } from '../../core/services/pipeline-runs.service';
import { NotificationService } from '../../core/notification/notification.service';
import { PipelineRunDto } from '../../core/models';
import { StatusBadge } from '../../shared/components/status-badge/status-badge';

@Component({
  selector: 'app-pipeline-runs',
  imports: [RouterLink, DatePipe, FormsModule, StatusBadge],
  templateUrl: './pipeline-runs.html',
})
export class PipelineRuns {
  private readonly route = inject(ActivatedRoute);
  private readonly pipelineRunsService = inject(PipelineRunsService);
  private readonly notifications = inject(NotificationService);

  readonly projectId = this.route.snapshot.paramMap.get('projectId')!;
  readonly runs = signal<PipelineRunDto[]>([]);
  readonly loading = signal(true);
  readonly triggerReason = signal('');

  constructor() {
    this.reload();
  }

  reload(): void {
    this.loading.set(true);
    this.pipelineRunsService.listForProject(this.projectId).subscribe({
      next: (runs) => {
        this.runs.set(runs);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  trigger(): void {
    const reason = this.triggerReason().trim();
    if (!reason) {
      return;
    }
    this.pipelineRunsService.trigger(this.projectId, { triggerReason: reason }).subscribe({
      next: (run) => {
        this.runs.update((runs) => [run, ...runs]);
        this.triggerReason.set('');
      },
    });
  }

  start(run: PipelineRunDto): void {
    this.pipelineRunsService.start(run.id).subscribe({ next: (updated) => this.patch(updated) });
  }

  complete(run: PipelineRunDto, succeeded: boolean): void {
    this.pipelineRunsService.complete(run.id, { succeeded, logOutput: null }).subscribe({
      next: (updated) => this.patch(updated),
    });
  }

  private patch(updated: PipelineRunDto): void {
    this.runs.update((runs) => runs.map((r) => (r.id === updated.id ? updated : r)));
    this.notifications.success(`Pipeline run ${updated.status.toLowerCase()}.`);
  }
}
