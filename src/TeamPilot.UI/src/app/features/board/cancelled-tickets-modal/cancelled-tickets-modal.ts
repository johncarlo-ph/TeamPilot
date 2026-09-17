import { DatePipe } from '@angular/common';
import { Component, effect, inject, input, output, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Modal } from '../../../shared/components/modal/modal';
import { TicketsService } from '../../../core/services/tickets.service';
import { TicketDto } from '../../../core/models';

@Component({
  selector: 'app-cancelled-tickets-modal',
  imports: [Modal, RouterLink, DatePipe],
  templateUrl: './cancelled-tickets-modal.html',
})
export class CancelledTicketsModal {
  private readonly ticketsService = inject(TicketsService);

  readonly open = input(false);
  readonly sprintId = input.required<string>();
  readonly closed = output<void>();

  readonly loading = signal(false);
  readonly tickets = signal<TicketDto[]>([]);

  constructor() {
    effect(() => {
      if (this.open()) {
        this.loading.set(true);
        this.ticketsService.listForSprint(this.sprintId(), 'Cancelled').subscribe({
          next: (tickets) => {
            this.tickets.set(tickets);
            this.loading.set(false);
          },
          error: () => this.loading.set(false),
        });
      }
    });
  }
}
