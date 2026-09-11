import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { AuditLogService } from '../../../core/services/audit-log.service';
import { AuditLogEntryDto } from '../../../core/models';

const PAGE_SIZE = 50;

@Component({
  selector: 'app-audit-log',
  imports: [DatePipe],
  templateUrl: './audit-log.html',
})
export class AuditLog {
  private readonly auditLogService = inject(AuditLogService);

  readonly entries = signal<AuditLogEntryDto[]>([]);
  readonly loading = signal(true);
  readonly skip = signal(0);
  readonly hasMore = signal(true);

  constructor() {
    this.loadPage();
  }

  loadPage(): void {
    this.loading.set(true);
    this.auditLogService.list(this.skip(), PAGE_SIZE).subscribe({
      next: (entries) => {
        this.entries.set(entries);
        this.hasMore.set(entries.length === PAGE_SIZE);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  next(): void {
    this.skip.update((skip) => skip + PAGE_SIZE);
    this.loadPage();
  }

  previous(): void {
    this.skip.update((skip) => Math.max(0, skip - PAGE_SIZE));
    this.loadPage();
  }
}
