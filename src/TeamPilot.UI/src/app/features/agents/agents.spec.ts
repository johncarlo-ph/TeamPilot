import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { Agents } from './agents';
import { WorkflowService } from '../../core/services/workflow.service';
import { TicketsService } from '../../core/services/tickets.service';
import { AuthService } from '../../core/services/auth.service';
import { InstructionsService } from '../../core/services/instructions.service';
import { InstructionTemplatesService } from '../../core/services/instruction-templates.service';
import { NotificationService } from '../../core/notification/notification.service';
import { AgentDto, TicketDto, WorkflowStageDto } from '../../core/models';

function agent(overrides: Partial<AgentDto> = {}): AgentDto {
  return {
    id: 'agent-1',
    projectId: 'project-1',
    name: 'Research Agent',
    role: 'Research',
    status: 'Active',
    configurationJson: '{}',
    createdAtUtc: new Date().toISOString(),
    updatedAtUtc: null,
    ...overrides,
  };
}

function stage(overrides: Partial<WorkflowStageDto> = {}): WorkflowStageDto {
  return {
    id: 'stage-1',
    projectId: 'project-1',
    order: 0,
    agent: agent(),
    loopBackToStageId: null,
    maxLoopIterations: null,
    ...overrides,
  };
}

describe('Agents', () => {
  let fixture: ComponentFixture<Agents>;
  let workflowService: {
    list: ReturnType<typeof vi.fn>;
    listUnscheduledAgents: ReturnType<typeof vi.fn>;
    deleteCustomAgent: ReturnType<typeof vi.fn>;
  };
  let ticketsService: { listForProject: ReturnType<typeof vi.fn> };
  let isAdmin = false;

  function setUp(stages: WorkflowStageDto[], inProgressTickets: TicketDto[] = [], unscheduledAgents: AgentDto[] = []): void {
    workflowService = {
      list: vi.fn().mockReturnValue(of(stages)),
      listUnscheduledAgents: vi.fn().mockReturnValue(of(unscheduledAgents)),
      deleteCustomAgent: vi.fn().mockReturnValue(of(undefined)),
    };
    ticketsService = {
      listForProject: vi.fn().mockReturnValue(of(inProgressTickets)),
    };

    TestBed.configureTestingModule({
      imports: [Agents],
      providers: [
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => 'project-1' } } } },
        { provide: WorkflowService, useValue: workflowService },
        { provide: TicketsService, useValue: ticketsService },
        { provide: AuthService, useValue: { isAdmin: () => isAdmin } },
        { provide: InstructionsService, useValue: { listForAgent: () => of([]) } },
        { provide: InstructionTemplatesService, useValue: { list: () => of([]) } },
        { provide: NotificationService, useValue: { success: vi.fn(), error: vi.fn() } },
      ],
    });

    fixture = TestBed.createComponent(Agents);
    fixture.detectChanges();
  }

  it('reload_StagesReturned_RendersEachStagesAgentNameAndRole', () => {
    isAdmin = false;
    setUp([stage({ agent: agent({ name: 'Research Agent', role: 'Research' }) }), stage({ id: 'stage-2', order: 1, agent: agent({ id: 'agent-2', name: 'Design Agent', role: 'Design' }) })]);

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Research Agent');
    expect(text).toContain('Design Agent');
  });

  it('render_NonAdminUser_HidesAddAndRemoveControls', () => {
    isAdmin = false;
    setUp([stage()]);

    const nativeElement = fixture.nativeElement as HTMLElement;
    expect(nativeElement.textContent).not.toContain('Add custom agent');
    expect(nativeElement.textContent).not.toContain('Remove');
  });

  it('render_AdminUser_ShowsAddAndRemoveControls', () => {
    isAdmin = true;
    setUp([stage()]);

    const nativeElement = fixture.nativeElement as HTMLElement;
    expect(nativeElement.textContent).toContain('Add custom agent');
    expect(nativeElement.textContent).toContain('Remove');
  });

  it('render_ProjectHasInProgressTicket_ShowsLockedBanner', () => {
    isAdmin = true;
    setUp([stage()], [{ id: 't1', status: 'InProgress' } as TicketDto]);

    expect(fixture.nativeElement.textContent).toContain('Pipeline is locked');
  });

  it('render_NoInProgressTickets_DoesNotShowLockedBanner', () => {
    isAdmin = true;
    setUp([stage()], []);

    expect(fixture.nativeElement.textContent).not.toContain('Pipeline is locked');
  });

  it('render_StageWithLoopBack_DescribesTheLoopBack', () => {
    isAdmin = false;
    const codingStage = stage({ id: 'coding', order: 0, agent: agent({ id: 'coding-agent', name: 'Coding Agent', role: 'Coding' }) });
    const testingStage = stage({
      id: 'testing',
      order: 1,
      agent: agent({ id: 'testing-agent', name: 'Testing Agent', role: 'Testing' }),
      loopBackToStageId: 'coding',
      maxLoopIterations: 3,
    });
    setUp([codingStage, testingStage]);

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Loops back to position 1');
    expect(text).toContain('3 attempt');
  });

  it('render_AdminUserWithUnscheduledCustomAgent_ShowsRemoveButtonForIt', () => {
    isAdmin = true;
    setUp([stage()], [], [agent({ id: 'custom-1', name: 'Reviewer', role: 'Custom' })]);

    const removeButtons = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('button')
    ).filter((b) => b.textContent?.trim() === 'Remove');
    // One for the scheduled stage, one for the unscheduled custom agent.
    expect(removeButtons.length).toBe(2);
  });

  it('render_AdminUserWithUnscheduledDefaultRoleAgent_DoesNotShowRemoveButtonForIt', () => {
    isAdmin = true;
    setUp([stage()], [], [agent({ id: 'design-1', name: 'Design Agent', role: 'Design' })]);

    const removeButtons = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('button')
    ).filter((b) => b.textContent?.trim() === 'Remove');
    // Only the scheduled stage's Remove button - the unscheduled default-role agent has none.
    expect(removeButtons.length).toBe(1);
  });

  it('deleteCustomAgent_WhenConfirmed_CallsServiceAndRemovesFromUnscheduledList', () => {
    isAdmin = true;
    const customAgent = agent({ id: 'custom-1', name: 'Reviewer', role: 'Custom' });
    setUp([stage()], [], [customAgent]);
    vi.spyOn(window, 'confirm').mockReturnValue(true);

    fixture.componentInstance.deleteCustomAgent(customAgent);

    expect(workflowService.deleteCustomAgent).toHaveBeenCalledWith('project-1', 'custom-1');
    expect(fixture.componentInstance.unscheduledAgents()).not.toContain(customAgent);
  });

  it('deleteCustomAgent_WhenNotConfirmed_DoesNotCallService', () => {
    isAdmin = true;
    const customAgent = agent({ id: 'custom-1', name: 'Reviewer', role: 'Custom' });
    setUp([stage()], [], [customAgent]);
    vi.spyOn(window, 'confirm').mockReturnValue(false);

    fixture.componentInstance.deleteCustomAgent(customAgent);

    expect(workflowService.deleteCustomAgent).not.toHaveBeenCalled();
  });
});
