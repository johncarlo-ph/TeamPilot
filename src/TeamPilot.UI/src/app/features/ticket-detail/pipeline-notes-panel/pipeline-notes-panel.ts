import { DatePipe } from '@angular/common';
import { Component, computed, input } from '@angular/core';
import { TicketPipelineNoteDto, TicketStatus } from '../../../core/models';

// Read-only: a pipeline stage's optional NOTES: marker (see backend OrchestrationService and
// TicketPipelineNote) surfaced for a human once the ticket reaches Done - there's no more raw
// agent output left to dig through at that point, so this is where a stage's summary/caveat/
// follow-up actually gets seen.
@Component({
  selector: 'app-pipeline-notes-panel',
  imports: [DatePipe],
  templateUrl: './pipeline-notes-panel.html',
})
export class PipelineNotesPanel {
  readonly notes = input<TicketPipelineNoteDto[]>([]);
  readonly status = input<TicketStatus | undefined>();

  readonly visible = computed(() => this.status() === 'Done' && this.notes().length > 0);

  readonly sortedNotes = computed(() =>
    [...this.notes()].sort((a, b) => new Date(b.createdAtUtc).getTime() - new Date(a.createdAtUtc).getTime())
  );
}
