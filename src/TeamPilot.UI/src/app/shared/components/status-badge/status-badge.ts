import { Component, computed, input } from '@angular/core';

const BADGE_CLASS_BY_VALUE: Record<string, string> = {
  // Ticket status — matches the accent color of the corresponding board column
  // (see .board-column--* and .text-bg-forreview in styles.scss)
  ToDo: 'text-bg-primary',
  InProgress: 'text-bg-warning',
  ForReview: 'text-bg-forreview',
  Done: 'text-bg-success',
  Cancelled: 'text-bg-dark',
  // Agent / user status
  Active: 'text-bg-success',
  Inactive: 'text-bg-secondary',
  Disabled: 'text-bg-secondary',
  // Conflict status
  Detected: 'text-bg-danger',
  AiResolutionSuggested: 'text-bg-warning',
  ResolvedManually: 'text-bg-success',
  ResolvedWithAiSuggestion: 'text-bg-success',
  // Pipeline run status
  Queued: 'text-bg-secondary',
  Running: 'text-bg-primary',
  Succeeded: 'text-bg-success',
  Failed: 'text-bg-danger',
  // Review decision
  Approve: 'text-bg-success',
  RequestChanges: 'text-bg-warning',
  ResolveConflict: 'text-bg-info',
};

@Component({
  selector: 'app-status-badge',
  template: `<span class="badge rounded-pill status-badge {{ badgeClass() }}">{{ label() }}</span>`,
})
export class StatusBadge {
  readonly value = input.required<string>();
  readonly label = computed(() => splitPascalCase(this.value()));
  readonly badgeClass = computed(() => BADGE_CLASS_BY_VALUE[this.value()] ?? 'text-bg-secondary');
}

function splitPascalCase(value: string): string {
  return value.replace(/([a-z])([A-Z])/g, '$1 $2');
}
