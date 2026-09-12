import { CdkDragDrop, DragDropModule, moveItemInArray } from '@angular/cdk/drag-drop';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { WorkflowService } from '../../core/services/workflow.service';
import { TicketsService } from '../../core/services/tickets.service';
import { AuthService } from '../../core/services/auth.service';
import { NotificationService } from '../../core/notification/notification.service';
import { AgentDto, AgentRole, WorkflowStageDto } from '../../core/models';
import { InstructionEditor } from './instruction-editor/instruction-editor';
import { Modal } from '../../shared/components/modal/modal';

@Component({
  selector: 'app-agents',
  imports: [RouterLink, DragDropModule, InstructionEditor, Modal],
  templateUrl: './agents.html',
})
export class Agents {
  private readonly route = inject(ActivatedRoute);
  private readonly workflowService = inject(WorkflowService);
  private readonly ticketsService = inject(TicketsService);
  protected readonly authService = inject(AuthService);
  private readonly notifications = inject(NotificationService);

  readonly projectId = this.route.snapshot.paramMap.get('projectId')!;
  readonly stages = signal<WorkflowStageDto[]>([]);
  readonly unscheduledAgents = signal<AgentDto[]>([]);
  readonly inProgressCount = signal(0);
  readonly loading = signal(true);
  readonly selectedAgentId = signal<string | null>(null);
  readonly selectedAgentRole = signal<AgentRole | null>(null);

  readonly newAgentModalOpen = signal(false);
  readonly newAgentName = signal('');

  readonly loopBackEditorStageId = signal<string | null>(null);
  readonly loopBackTargetId = signal('');
  readonly loopBackMaxIterations = signal(3);

  readonly locked = computed(() => this.inProgressCount() > 0);

  constructor() {
    this.reload();
  }

  loopBackTargetsFor(stage: WorkflowStageDto): WorkflowStageDto[] {
    return this.stages().filter((s) => s.order < stage.order);
  }

  stageForLoopBackTarget(targetId: string | null): WorkflowStageDto | undefined {
    return targetId ? this.stages().find((s) => s.id === targetId) : undefined;
  }

  selectAgent(agent: AgentDto): void {
    this.selectedAgentId.set(agent.id);
    this.selectedAgentRole.set(agent.role);
  }

  onReorder(event: CdkDragDrop<WorkflowStageDto[]>): void {
    if (this.locked() || !this.authService.isAdmin() || event.previousIndex === event.currentIndex) {
      return;
    }

    const reordered = [...this.stages()];
    moveItemInArray(reordered, event.previousIndex, event.currentIndex);

    this.workflowService.reorder(this.projectId, { stageIds: reordered.map((s) => s.id) }).subscribe({
      next: (stages) => this.stages.set(stages),
      error: () => this.reload(),
    });
  }

  openNewAgentModal(): void {
    this.newAgentName.set('');
    this.newAgentModalOpen.set(true);
  }

  createCustomAgent(): void {
    const name = this.newAgentName().trim();
    if (!name) {
      return;
    }

    this.workflowService.createCustomAgent(this.projectId, { name }).subscribe({
      next: (agent) => {
        this.unscheduledAgents.update((agents) => [...agents, agent]);
        this.newAgentModalOpen.set(false);
        this.notifications.success(`'${agent.name}' created - add its instructions, then add it to the pipeline.`);
        this.selectAgent(agent);
      },
    });
  }

  addToPipeline(agent: AgentDto): void {
    this.workflowService.addStage(this.projectId, { agentId: agent.id }).subscribe({
      next: () => {
        this.notifications.success(`'${agent.name}' added to the pipeline.`);
        this.reload();
      },
    });
  }

  deleteCustomAgent(agent: AgentDto): void {
    if (!confirm(`Permanently delete '${agent.name}'? It isn't in the pipeline, so this can't be undone.`)) {
      return;
    }

    this.workflowService.deleteCustomAgent(this.projectId, agent.id).subscribe({
      next: () => {
        this.unscheduledAgents.update((agents) => agents.filter((a) => a.id !== agent.id));
        if (this.selectedAgentId() === agent.id) {
          this.selectedAgentId.set(null);
          this.selectedAgentRole.set(null);
        }
        this.notifications.success(`'${agent.name}' deleted.`);
      },
    });
  }

  removeStage(stage: WorkflowStageDto): void {
    if (!confirm(`Remove '${stage.agent.name}' from the pipeline? Its instructions are kept and it can be re-added later.`)) {
      return;
    }

    this.workflowService.removeStage(this.projectId, stage.id).subscribe({
      next: () => {
        this.notifications.success(`'${stage.agent.name}' removed from the pipeline.`);
        this.reload();
      },
    });
  }

  openLoopBackEditor(stage: WorkflowStageDto): void {
    this.loopBackEditorStageId.set(stage.id);
    this.loopBackTargetId.set(stage.loopBackToStageId ?? '');
    this.loopBackMaxIterations.set(stage.maxLoopIterations ?? 3);
  }

  cancelLoopBackEditor(): void {
    this.loopBackEditorStageId.set(null);
  }

  saveLoopBack(stage: WorkflowStageDto): void {
    const targetStageId = this.loopBackTargetId();
    if (!targetStageId) {
      return;
    }

    this.workflowService
      .setLoopBack(this.projectId, stage.id, {
        targetStageId,
        maxLoopIterations: this.loopBackMaxIterations(),
      })
      .subscribe({
        next: (updated) => {
          this.patchStage(updated);
          this.loopBackEditorStageId.set(null);
          this.notifications.success('Loop-back saved.');
        },
      });
  }

  clearLoopBack(stage: WorkflowStageDto): void {
    this.workflowService.clearLoopBack(this.projectId, stage.id).subscribe({
      next: (updated) => {
        this.patchStage(updated);
        this.notifications.success('Loop-back cleared.');
      },
    });
  }

  private patchStage(updated: WorkflowStageDto): void {
    this.stages.update((stages) => stages.map((s) => (s.id === updated.id ? updated : s)));
  }

  private reload(): void {
    this.loading.set(true);

    this.workflowService.list(this.projectId).subscribe({
      next: (stages) => {
        this.stages.set(stages);
        this.loading.set(false);
        if (!this.selectedAgentId() && stages.length) {
          this.selectAgent(stages[0].agent);
        }
      },
      error: () => this.loading.set(false),
    });

    this.workflowService.listUnscheduledAgents(this.projectId).subscribe((agents) => this.unscheduledAgents.set(agents));

    this.ticketsService.listForProject(this.projectId, 'InProgress').subscribe((tickets) => this.inProgressCount.set(tickets.length));
  }
}
