import { Component, computed, input } from '@angular/core';

const BADGE_CLASS_BY_VALUE: Record<string, string> = {
  // Ticket status — matches the accent color of the corresponding board column
  // (see .board-column--* and .text-bg-forreview in styles.scss)
  ToDo: 'text-bg-primary',
  InProgress: 'text-bg-warning',
  ForReview: 'text-bg-forreview',
  Done: 'text-bg-success',
  Cancelled: 'text-bg-danger',
  Blocked: 'text-bg-danger',
  // Ticket question kind/status
  Question: 'text-bg-warning',
  Failure: 'text-bg-danger',
  Decision: 'text-bg-info',
  Pending: 'text-bg-warning',
  Answered: 'text-bg-success',
  // Ticket agent event kind
  Started: 'text-bg-info',
  Completed: 'text-bg-success',
  Failed: 'text-bg-danger',
  // Agent / user status
  Active: 'text-bg-success',
  Inactive: 'text-bg-secondary',
  Disabled: 'text-bg-secondary',
  // Conflict status
  Detected: 'text-bg-danger',
  AiResolutionSuggested: 'text-bg-warning',
  ResolvedManually: 'text-bg-success',
  ResolvedWithAiSuggestion: 'text-bg-success',
  // Review decision
  Approve: 'text-bg-success',
  RequestChanges: 'text-bg-warning',
  ResolveConflict: 'text-bg-info',
};

// Ticket status only - matches the Kanban board's own column icons (see BOARD_COLUMNS in
// features/board/board.ts, which imports this rather than redeclaring the emoji). Every other
// status-badge value (review decisions, conflict/question/user status, ...) has no entry here and
// simply renders without an icon.
export const TICKET_STATUS_ICONS: Record<string, string> = {
  ToDo: '⏳',
  InProgress: '🔧',
  Blocked: '🚫',
  ForReview: '👀',
  Done: '✅',
};

@Component({
  selector: 'app-status-badge',
  template: `<span class="badge rounded-pill status-badge {{ badgeClass() }}"
    >@if (icon(); as icon) {
      <span aria-hidden="true">{{ icon }}</span>
    }{{ label() }}</span
  >`,
})
export class StatusBadge {
  readonly value = input.required<string>();
  readonly label = computed(() => splitPascalCase(this.value()));
  readonly badgeClass = computed(() => BADGE_CLASS_BY_VALUE[this.value()] ?? 'text-bg-secondary');
  readonly icon = computed(() => TICKET_STATUS_ICONS[this.value()] ?? null);
}

function splitPascalCase(value: string): string {
  return value.replace(/([a-z])([A-Z])/g, '$1 $2');
}
