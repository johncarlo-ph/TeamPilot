import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { MentionAutocomplete } from './mention-autocomplete';
import { MentionsService } from '../../../core/services/mentions.service';
import { FileMentionResultDto, ProjectMentionResultDto, TicketMentionResultDto } from '../../../core/models';

function ticketResult(overrides: Partial<TicketMentionResultDto> = {}): TicketMentionResultDto {
  return { id: 'ticket-1', title: 'Fix login bug', projectId: 'project-1', projectName: 'TeamPilot', ...overrides };
}

function projectResult(overrides: Partial<ProjectMentionResultDto> = {}): ProjectMentionResultDto {
  return { id: 'project-2', name: 'Billing Service', ...overrides };
}

function fileResult(overrides: Partial<FileMentionResultDto> = {}): FileMentionResultDto {
  return { path: 'src/app/foo.ts', ...overrides };
}

// No zone.js/fakeAsync in this project's (zoneless) test setup - the debounce is awaited for
// real instead of simulated, matching this spec file's own simplicity/reliability trade-off.
const DEBOUNCE_SETTLE_MS = 300;

function waitForDebounce(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, DEBOUNCE_SETTLE_MS));
}

describe('MentionAutocomplete', () => {
  let fixture: ComponentFixture<MentionAutocomplete>;
  let mentionsService: {
    searchTickets: ReturnType<typeof vi.fn>;
    searchProjects: ReturnType<typeof vi.fn>;
    searchFiles: ReturnType<typeof vi.fn>;
  };

  function setUp(query: string, projects: { id: string; name: string }[] = []): void {
    mentionsService = {
      searchTickets: vi.fn().mockReturnValue(of([ticketResult()])),
      searchProjects: vi.fn().mockReturnValue(of([projectResult()])),
      searchFiles: vi.fn().mockReturnValue(of([fileResult()])),
    };

    TestBed.configureTestingModule({
      imports: [MentionAutocomplete],
      providers: [{ provide: MentionsService, useValue: mentionsService }],
    });

    fixture = TestBed.createComponent(MentionAutocomplete);
    fixture.componentRef.setInput('query', query);
    fixture.componentRef.setInput('projects', projects);
    fixture.detectChanges();
  }

  it('constructor_NoCategoryChosen_ShowsCategoryPickerAndNeverSearches', async () => {
    setUp('billing');
    await waitForDebounce();

    expect(fixture.componentInstance.category()).toBeNull();
    expect(mentionsService.searchTickets).not.toHaveBeenCalled();
    expect(mentionsService.searchProjects).not.toHaveBeenCalled();
    expect(mentionsService.searchFiles).not.toHaveBeenCalled();
  });

  it('constructor_TicketCategoryChosenQueryTooShort_NeverCallsSearch', async () => {
    setUp('b');
    fixture.componentInstance.selectCategory('ticket');
    await waitForDebounce();

    expect(mentionsService.searchTickets).not.toHaveBeenCalled();
  });

  it('constructor_TicketCategoryChosen_SearchesOnlyTicketsAfterDebounce', async () => {
    setUp('bil');
    fixture.componentInstance.selectCategory('ticket');
    await waitForDebounce();

    expect(mentionsService.searchTickets).toHaveBeenCalledWith('bil');
    expect(mentionsService.searchProjects).not.toHaveBeenCalled();
    expect(fixture.componentInstance.results()).toHaveLength(1);
    expect(fixture.componentInstance.results()[0]).toMatchObject({ type: 'ticket', id: 'ticket-1', label: 'Fix login bug' });
  });

  it('constructor_ProjectCategoryChosen_SearchesOnlyProjectsAfterDebounce', async () => {
    setUp('bil');
    fixture.componentInstance.selectCategory('project');
    await waitForDebounce();

    expect(mentionsService.searchProjects).toHaveBeenCalledWith('bil');
    expect(mentionsService.searchTickets).not.toHaveBeenCalled();
    expect(fixture.componentInstance.results()[0]).toMatchObject({ type: 'project', id: 'project-2', label: 'Billing Service' });
  });

  it('constructor_FileCategoryChosen_SearchesEveryCandidateProjectAndTagsResultsWithIt', async () => {
    setUp('foo', [
      { id: 'own-project', name: 'TeamPilot' },
      { id: 'other-project', name: 'Billing Service' },
    ]);
    fixture.componentInstance.selectCategory('file');
    await waitForDebounce();

    expect(mentionsService.searchFiles).toHaveBeenCalledWith('own-project', 'foo');
    expect(mentionsService.searchFiles).toHaveBeenCalledWith('other-project', 'foo');
    expect(fixture.componentInstance.results()).toHaveLength(2);
    expect(fixture.componentInstance.results()[0]).toMatchObject({
      type: 'file',
      id: 'own-project',
      label: 'src/app/foo.ts',
      filePath: 'src/app/foo.ts',
      meta: 'TeamPilot',
    });
  });

  it('handleKeydown_ArrowDownInCategoryPicker_MovesHighlightAcrossTheThreeCategories', () => {
    setUp('');
    const component = fixture.componentInstance;
    const event = { key: 'ArrowDown', preventDefault: vi.fn() } as unknown as KeyboardEvent;

    component.handleKeydown(event);
    expect(component.highlightedIndex()).toBe(1);

    component.handleKeydown(event);
    expect(component.highlightedIndex()).toBe(2);

    // Clamped at the last category (3 total: Project, Ticket, File).
    component.handleKeydown(event);
    expect(component.highlightedIndex()).toBe(2);
  });

  it('handleKeydown_EnterInCategoryPicker_SelectsHighlightedCategoryWithoutEmittingSelected', () => {
    setUp('');
    const component = fixture.componentInstance;
    const selected = vi.fn();
    component.selected.subscribe(selected);
    const down = { key: 'ArrowDown', preventDefault: vi.fn() } as unknown as KeyboardEvent;
    const enter = { key: 'Enter', preventDefault: vi.fn() } as unknown as KeyboardEvent;

    component.handleKeydown(down); // highlight -> Ticket (index 1)
    component.handleKeydown(enter);

    expect(component.category()).toBe('ticket');
    expect(selected).not.toHaveBeenCalled();
  });

  it('handleKeydown_ArrowDownInSearchResults_MovesHighlightWithoutExceedingLastIndex', async () => {
    setUp('bil');
    fixture.componentInstance.selectCategory('ticket');
    await waitForDebounce();
    const component = fixture.componentInstance;
    const event = { key: 'ArrowDown', preventDefault: vi.fn() } as unknown as KeyboardEvent;

    component.handleKeydown(event);
    expect(component.highlightedIndex()).toBe(0); // only one result

    component.handleKeydown(event);
    expect(component.highlightedIndex()).toBe(0);
  });

  it('handleKeydown_EnterWithSearchResults_EmitsSelectedForHighlightedItem', async () => {
    setUp('bil');
    fixture.componentInstance.selectCategory('ticket');
    await waitForDebounce();
    const component = fixture.componentInstance;
    const selected = vi.fn();
    component.selected.subscribe(selected);
    const event = { key: 'Enter', preventDefault: vi.fn() } as unknown as KeyboardEvent;

    const handled = component.handleKeydown(event);

    expect(handled).toBe(true);
    expect(selected).toHaveBeenCalledWith(
      expect.objectContaining({ type: 'ticket', id: 'ticket-1', label: 'Fix login bug' })
    );
  });

  it('handleKeydown_Escape_EmitsClosed', () => {
    setUp('bil');
    const component = fixture.componentInstance;
    const closed = vi.fn();
    component.closed.subscribe(closed);
    const event = { key: 'Escape', preventDefault: vi.fn() } as unknown as KeyboardEvent;

    const handled = component.handleKeydown(event);

    expect(handled).toBe(true);
    expect(closed).toHaveBeenCalled();
  });

  it('handleKeydown_UnhandledKey_ReturnsFalse', () => {
    setUp('bil');
    const event = { key: 'a', preventDefault: vi.fn() } as unknown as KeyboardEvent;

    expect(fixture.componentInstance.handleKeydown(event)).toBe(false);
  });
});
