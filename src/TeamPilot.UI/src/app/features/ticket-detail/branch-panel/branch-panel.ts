import { Component, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { GitService } from '../../../core/services/git.service';
import { NotificationService } from '../../../core/notification/notification.service';
import { GitDiffResult, TicketDto } from '../../../core/models';
import { DiffViewer } from '../../../shared/components/diff-viewer/diff-viewer';

@Component({
  selector: 'app-branch-panel',
  imports: [FormsModule, DiffViewer],
  templateUrl: './branch-panel.html',
})
export class BranchPanel {
  private readonly gitService = inject(GitService);
  private readonly notifications = inject(NotificationService);

  readonly ticket = input.required<TicketDto>();
  readonly branchCreated = output<TicketDto>();

  readonly newBranchName = signal('');
  readonly sourceBranch = signal('main');
  readonly targetBranch = signal('');
  readonly diffResult = signal<GitDiffResult | null>(null);
  readonly diffLoading = signal(false);

  createBranch(): void {
    const branchName = this.newBranchName().trim();
    if (!branchName) {
      return;
    }
    this.gitService.createBranch({ ticketId: this.ticket().id, branchName }).subscribe({
      next: (updated) => {
        this.notifications.success('Branch linked to ticket.');
        this.newBranchName.set('');
        this.branchCreated.emit(updated);
      },
    });
  }

  loadDiff(): void {
    const target = this.targetBranch().trim() || this.ticket().branchName;
    if (!target) {
      this.notifications.error('Set a target branch (or link a branch to this ticket) first.');
      return;
    }
    this.diffLoading.set(true);
    this.gitService.diff(this.ticket().projectId, this.sourceBranch().trim(), target).subscribe({
      next: (result) => {
        this.diffResult.set(result);
        this.diffLoading.set(false);
      },
      error: () => this.diffLoading.set(false),
    });
  }
}
