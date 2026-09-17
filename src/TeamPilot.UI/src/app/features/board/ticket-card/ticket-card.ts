import { DatePipe } from '@angular/common';
import { Component, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TicketDto } from '../../../core/models';

@Component({
  selector: 'app-ticket-card',
  imports: [RouterLink, DatePipe],
  templateUrl: './ticket-card.html',
})
export class TicketCard {
  readonly ticket = input.required<TicketDto>();
  // Only ever shown/emitted for a To Do ticket (see ticket-card.html) - TicketService.moveToBacklog
  // itself also only allows it from To Do with no linked branch.
  readonly movedToBacklog = output<void>();
}
