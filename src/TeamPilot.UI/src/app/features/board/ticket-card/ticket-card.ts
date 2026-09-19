import { DatePipe } from '@angular/common';
import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TicketDto } from '../../../core/models';

@Component({
  selector: 'app-ticket-card',
  imports: [RouterLink, DatePipe],
  templateUrl: './ticket-card.html',
})
export class TicketCard {
  readonly ticket = input.required<TicketDto>();
}
