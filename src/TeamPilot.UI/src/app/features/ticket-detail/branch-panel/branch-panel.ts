import { Component, computed, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { GitService } from '../../../core/services/git.service';
import { NotificationService } from '../../../core/notification/notification.service';
import { GitDiffResult, TicketDto } from '../../../core/models';
import { DiffViewer } from '../../../shared/components/diff-viewer/diff-viewer';
import { Modal } from '../../../shared/components/modal/modal';
import { buildBranchUrl } from '../../../core/utils/git-url.util';

@Component({
  selector: 'app-branch-panel',
  imports: [FormsModule, DiffViewer, Modal],
  templateUrl: './branch-panel.html',
})
export class BranchPanel {
  private readonly gitService = inject(GitService);
  private readonly notifications = inject(NotificationService);

  readonly ticket = input.required<TicketDto>();
  readonly remoteUrl = input<string | null>(null);
  readonly changed = output<TicketDto>();

  readonly linkedBranchUrl = computed(() => buildBranchUrl(this.remoteUrl(), this.ticket().branchName));

  readonly newBranchName = signal('');
  readonly sourceBranch = signal('main');
  readonly targetBranch = signal('');
  readonly diffResult = signal<GitDiffResult | null>(null);
  readonly diffLoading = signal(false);

  readonly checkingBranch = signal(false);
  readonly confirmModalOpen = signal(false);
  readonly pendingBranchName = signal('');
  readonly branchAlreadyExists = signal(false);
  readonly linkingBranch = signal(false);
  readonly deletingBranch = signal(false);

  createBranch(): void {
    const branchName = this.newBranchName().trim();
    if (!branchName) {
      return;
    }
    this.checkingBranch.set(true);
    this.gitService.branchExists(this.ticket().id, branchName).subscribe({
      next: (exists) => {
        this.checkingBranch.set(false);
        this.pendingBranchName.set(branchName);
        this.branchAlreadyExists.set(exists);
        this.confirmModalOpen.set(true);
      },
      error: () => this.checkingBranch.set(false),
    });
  }

  confirmLinkBranch(): void {
    const branchName = this.pendingBranchName();
    this.linkingBranch.set(true);
    this.gitService.createBranch({ ticketId: this.ticket().id, branchName }).subscribe({
      next: (updated) => {
        this.linkingBranch.set(false);
        this.notifications.success(
          this.branchAlreadyExists() ? 'Existing branch linked to ticket.' : 'New branch created and linked to ticket.'
        );
        this.newBranchName.set('');
        this.confirmModalOpen.set(false);
        this.changed.emit(updated);
      },
      error: () => this.linkingBranch.set(false),
    });
  }

  cancelLinkBranch(): void {
    this.confirmModalOpen.set(false);
  }

  deleteBranch(): void {
    const ticket = this.ticket();
    const branchName = ticket.branchName;
    if (!branchName || !confirm(`Delete branch "${branchName}"? This cannot be undone.`)) {
      return;
    }
    this.deletingBranch.set(true);
    this.gitService.deleteBranch(ticket.id).subscribe({
      next: (updated) => {
        this.deletingBranch.set(false);
        this.notifications.success('Branch deleted.');
        this.changed.emit(updated);
      },
      error: () => this.deletingBranch.set(false),
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
