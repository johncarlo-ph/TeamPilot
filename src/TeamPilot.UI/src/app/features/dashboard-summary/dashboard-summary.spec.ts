import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { DashboardSummary } from './dashboard-summary';
import { DashboardService } from '../../core/services/dashboard.service';
import { DashboardSummaryDto } from '../../core/models';

function summary(overrides: Partial<DashboardSummaryDto> = {}): DashboardSummaryDto {
  return {
    activeSprintCount: 2,
    ticketStatusCounts: { toDo: 1, inProgress: 2, blocked: 0, forReview: 0, done: 3 },
    needsAttention: { blockedTickets: [], ticketsForReview: [], sprintsAtRisk: [] },
    ...overrides,
  };
}

describe('DashboardSummary', () => {
  let fixture: ComponentFixture<DashboardSummary>;
  let dashboardService: { getSummary: ReturnType<typeof vi.fn> };

  function setUp(response = summary()): void {
    dashboardService = { getSummary: vi.fn().mockReturnValue(of(response)) };

    TestBed.configureTestingModule({
      imports: [DashboardSummary],
      providers: [provideRouter([]), { provide: DashboardService, useValue: dashboardService }],
    });

    fixture = TestBed.createComponent(DashboardSummary);
    fixture.detectChanges();
  }

  it('reload_SummaryReturned_RendersActiveSprintCountAndStatusCounts', () => {
    setUp(summary({ activeSprintCount: 4 }));

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('4');
    expect(text).toContain('Active Sprints');
  });

  it('reload_NoTicketsNeedAttention_ShowsEmptyState', () => {
    setUp();

    expect(fixture.nativeElement.textContent).toContain('Nothing needs attention right now.');
  });

  it('reload_BlockedAndForReviewTicketsPresent_RendersEachInItsOwnList', () => {
    setUp(
      summary({
        needsAttention: {
          blockedTickets: [
            {
              id: 't1',
              title: 'Blocked ticket',
              status: 'Blocked',
              projectId: 'p1',
              projectName: 'Project One',
              sprintId: null,
              sprintName: null,
            },
          ],
          ticketsForReview: [
            {
              id: 't2',
              title: 'Review ticket',
              status: 'ForReview',
              projectId: 'p1',
              projectName: 'Project One',
              sprintId: 's1',
              sprintName: 'Sprint 1',
            },
          ],
          sprintsAtRisk: [],
        },
      })
    );

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Blocked ticket');
    expect(text).toContain('Review ticket');
    expect(text).toContain('Sprint 1');
  });

  it('reload_SprintsAtRiskPresent_RendersUnfinishedCount', () => {
    setUp(
      summary({
        needsAttention: {
          blockedTickets: [],
          ticketsForReview: [],
          sprintsAtRisk: [
            {
              id: 's1',
              name: 'Sprint 1',
              projectId: 'p1',
              projectName: 'Project One',
              sprintEndDate: new Date().toISOString(),
              unfinishedTicketCount: 5,
            },
          ],
        },
      })
    );

    expect(fixture.nativeElement.textContent).toContain('5 unfinished');
  });

  it('reload_ServiceErrors_StopsLoadingWithoutThrowing', () => {
    dashboardService = { getSummary: vi.fn().mockReturnValue(throwError(() => new Error('boom'))) };

    TestBed.configureTestingModule({
      imports: [DashboardSummary],
      providers: [provideRouter([]), { provide: DashboardService, useValue: dashboardService }],
    });

    fixture = TestBed.createComponent(DashboardSummary);
    fixture.detectChanges();

    expect(fixture.componentInstance.loading()).toBe(false);
  });
});
