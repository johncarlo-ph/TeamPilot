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

  /** Token usage + wall-clock duration for this stage attempt, e.g. "120 in / 340 out tokens ·
   * 5.5s" - null (nothing rendered) for a Started event, which has neither yet. */
  meta(event: TicketAgentEventDto): string | null {
    const parts: string[] = [];
    if (event.inputTokens !== null && event.outputTokens !== null) {
      parts.push(`${event.inputTokens} in / ${event.outputTokens} out tokens`);
    }
    if (event.durationMs !== null) {
      parts.push(event.durationMs >= 1000 ? `${(event.durationMs / 1000).toFixed(1)}s` : `${event.durationMs}ms`);
    }
    return parts.length ? parts.join(' · ') : null;
  }
}
