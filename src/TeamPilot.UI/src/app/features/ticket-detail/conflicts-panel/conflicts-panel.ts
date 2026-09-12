import { DatePipe } from '@angular/common';
import { Component, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ConflictsService } from '../../../core/services/conflicts.service';
import { AuthService } from '../../../core/services/auth.service';
import { NotificationService } from '../../../core/notification/notification.service';
import { ConflictDto } from '../../../core/models';
import { DiffViewer } from '../../../shared/components/diff-viewer/diff-viewer';
import { StatusBadge } from '../../../shared/components/status-badge/status-badge';

@Component({
  selector: 'app-conflicts-panel',
  imports: [FormsModule, DiffViewer, StatusBadge, DatePipe],
  templateUrl: './conflicts-panel.html',
})
export class ConflictsPanel {
  private readonly conflictsService = inject(ConflictsService);
  private readonly authService = inject(AuthService);
  private readonly notifications = inject(NotificationService);

  readonly ticketId = input.required<string>();
  readonly conflicts = input<ConflictDto[]>([]);
  readonly changed = output<void>();

  readonly resolvingNoteByConflictId = signal<Record<string, string>>({});
  readonly resolvingContentByConflictId = signal<Record<string, string>>({});
  readonly detecting = signal(false);

  get resolvedByName(): string {
    return this.authService.currentUser()?.name ?? '';
  }

  noteFor(conflictId: string): string {
    return this.resolvingNoteByConflictId()[conflictId] ?? '';
  }

  setNote(conflictId: string, value: string): void {
    this.resolvingNoteByConflictId.update((notes) => ({ ...notes, [conflictId]: value }));
  }

  // Defaults to the raw conflicting content so the user edits down to a final version (removing
  // the <<<<<<< markers) rather than typing a whole file from scratch.
  contentFor(conflict: ConflictDto): string {
    return this.resolvingContentByConflictId()[conflict.id] ?? conflict.conflictingDiffContent;
  }

  setContent(conflictId: string, value: string): void {
    this.resolvingContentByConflictId.update((values) => ({ ...values, [conflictId]: value }));
  }

  detectConflicts(): void {
    this.detecting.set(true);
    this.conflictsService.detect(this.ticketId()).subscribe({
      next: () => {
        this.detecting.set(false);
        this.changed.emit();
      },
      error: () => this.detecting.set(false),
    });
  }

  suggestResolution(conflict: ConflictDto): void {
    this.conflictsService.suggestResolution(conflict.id).subscribe({
      next: () => this.changed.emit(),
    });
  }

  acceptAiSuggestion(conflict: ConflictDto): void {
    this.conflictsService
      .acceptAiSuggestion(conflict.id, { resolvedBy: this.resolvedByName })
      .subscribe({
        next: () => {
          this.notifications.success('AI suggestion accepted.');
          this.changed.emit();
        },
      });
  }

  resolveManually(conflict: ConflictDto): void {
    const resolvedContent = this.contentFor(conflict).trim();
    if (!resolvedContent) {
      this.notifications.error('Enter the resolved file content first.');
      return;
    }
    const note = this.noteFor(conflict.id).trim() || null;
    this.conflictsService
      .resolveManually(conflict.id, { resolvedContent, note, resolvedBy: this.resolvedByName })
      .subscribe({
        next: () => {
          this.notifications.success('Conflict resolved.');
          this.changed.emit();
        },
      });
  }
}
