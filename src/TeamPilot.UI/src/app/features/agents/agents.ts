import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AgentsService } from '../../core/services/agents.service';
import { NotificationService } from '../../core/notification/notification.service';
import { AgentDto, AgentStatus, CreateAgentRequest } from '../../core/models';
import { StatusBadge } from '../../shared/components/status-badge/status-badge';
import { AgentForm } from './agent-form/agent-form';
import { InstructionEditor } from './instruction-editor/instruction-editor';

@Component({
  selector: 'app-agents',
  imports: [RouterLink, StatusBadge, AgentForm, InstructionEditor],
  templateUrl: './agents.html',
})
export class Agents {
  private readonly route = inject(ActivatedRoute);
  private readonly agentsService = inject(AgentsService);
  private readonly notifications = inject(NotificationService);

  readonly projectId = this.route.snapshot.paramMap.get('projectId')!;
  readonly agents = signal<AgentDto[]>([]);
  readonly loading = signal(true);
  readonly formOpen = signal(false);
  readonly selectedAgentId = signal<string | null>(null);

  constructor() {
    this.reload();
  }

  reload(): void {
    this.loading.set(true);
    this.agentsService.listForProject(this.projectId).subscribe({
      next: (agents) => {
        this.agents.set(agents);
        this.loading.set(false);
        if (!this.selectedAgentId() && agents.length) {
          this.selectedAgentId.set(agents[0].id);
        }
      },
      error: () => this.loading.set(false),
    });
  }

  create(request: CreateAgentRequest): void {
    this.agentsService.create(this.projectId, request).subscribe({
      next: (agent) => {
        this.agents.update((agents) => [...agents, agent]);
        this.selectedAgentId.set(agent.id);
        this.formOpen.set(false);
        this.notifications.success('Agent created.');
      },
    });
  }

  toggleStatus(agent: AgentDto): void {
    const status: AgentStatus = agent.status === 'Active' ? 'Inactive' : 'Active';
    this.agentsService.updateStatus(agent.id, { status }).subscribe({
      next: (updated) => {
        this.agents.update((agents) => agents.map((a) => (a.id === updated.id ? updated : a)));
      },
    });
  }
}
