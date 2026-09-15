import { DatePipe } from '@angular/common';
import { Component, input } from '@angular/core';
import { TicketAgentEventDto } from '../../../core/models';
import { StatusBadge } from '../../../shared/components/status-badge/status-badge';

const VERB_BY_KIND: Record<TicketAgentEventDto['kind'], string> = {
  Started: 'started',
  Completed: 'completed',
  Blocked: 'paused',
  Failed: 'failed',
};

@Component({
  selector: 'app-agent-event-log-panel',
  imports: [DatePipe, StatusBadge],
  templateUrl: './agent-event-log-panel.html',
})
export class AgentEventLogPanel {
  readonly events = input<TicketAgentEventDto[]>([]);

  label(event: TicketAgentEventDto): string {
    const subject = event.role ? `${event.role} agent` : 'Pipeline';
    return `${subject} ${VERB_BY_KIND[event.kind]}`;
  }
}
