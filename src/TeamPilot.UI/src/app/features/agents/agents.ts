import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AgentsService } from '../../core/services/agents.service';
import { AgentDto, AgentRole, AGENT_ROLES } from '../../core/models';
import { InstructionEditor } from './instruction-editor/instruction-editor';

const PIPELINE_ROLE_ORDER: AgentRole[] = AGENT_ROLES.filter((role) => role !== 'LiveAgent');

@Component({
  selector: 'app-agents',
  imports: [RouterLink, InstructionEditor],
  templateUrl: './agents.html',
})
export class Agents {
  private readonly route = inject(ActivatedRoute);
  private readonly agentsService = inject(AgentsService);

  readonly projectId = this.route.snapshot.paramMap.get('projectId')!;
  readonly agents = signal<AgentDto[]>([]);
  readonly loading = signal(true);
  readonly selectedAgentId = signal<string | null>(null);

  readonly pipelineAgents = computed(() =>
    this.agents()
      .filter((agent) => agent.role !== 'LiveAgent')
      .sort((a, b) => PIPELINE_ROLE_ORDER.indexOf(a.role) - PIPELINE_ROLE_ORDER.indexOf(b.role)),
  );
  readonly selectedAgent = computed(() => this.agents().find((a) => a.id === this.selectedAgentId()) ?? null);

  constructor() {
    this.reload();
  }

  reload(): void {
    this.loading.set(true);
    this.agentsService.listForProject(this.projectId).subscribe({
      next: (agents) => {
        this.agents.set(agents);
        this.loading.set(false);
        if (!this.selectedAgentId()) {
          const first = this.pipelineAgents()[0];
          if (first) {
            this.selectedAgentId.set(first.id);
          }
        }
      },
      error: () => this.loading.set(false),
    });
  }
}
