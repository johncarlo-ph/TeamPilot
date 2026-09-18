import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DashboardService } from '../../core/services/dashboard.service';
import { DashboardSummaryDto } from '../../core/models';
import { StatusBadge } from '../../shared/components/status-badge/status-badge';

interface StatusCount {
  status: string;
  count: number;
}

@Component({
  selector: 'app-dashboard-summary',
  imports: [RouterLink, StatusBadge, DatePipe],
  templateUrl: './dashboard-summary.html',
})
export class DashboardSummary {
  private readonly dashboardService = inject(DashboardService);

  readonly summary = signal<DashboardSummaryDto | null>(null);
  readonly loading = signal(true);

  readonly statusCounts = computed<StatusCount[]>(() => {
    const counts = this.summary()?.ticketStatusCounts;
    if (!counts) {
      return [];
    }
    return [
      { status: 'ToDo', count: counts.toDo },
      { status: 'InProgress', count: counts.inProgress },
      { status: 'ForReview', count: counts.forReview },
      { status: 'Blocked', count: counts.blocked },
      { status: 'Done', count: counts.done },
    ];
  });

  readonly hasNothingToAttendTo = computed(() => {
    const needsAttention = this.summary()?.needsAttention;
    return (
      !!needsAttention &&
      !needsAttention.blockedTickets.length &&
      !needsAttention.ticketsForReview.length &&
      !needsAttention.sprintsAtRisk.length
    );
  });

  constructor() {
    this.dashboardService.getSummary().subscribe({
      next: (summary) => {
        this.summary.set(summary);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }
}
