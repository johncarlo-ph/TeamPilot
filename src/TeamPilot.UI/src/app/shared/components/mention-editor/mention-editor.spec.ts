import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { MentionEditor } from './mention-editor';
import { MentionsService } from '../../../core/services/mentions.service';
import { getCanonicalText } from './mention-editor.dom';

function setCaret(node: Node, offset: number): void {
  const range = document.createRange();
  range.setStart(node, offset);
  range.collapse(true);
  const selection = window.getSelection()!;
  selection.removeAllRanges();
  selection.addRange(range);
}

describe('MentionEditor', () => {
  let fixture: ComponentFixture<MentionEditor>;
  let root: HTMLElement;

  function setUp(): void {
    TestBed.configureTestingModule({
      imports: [MentionEditor],
      providers: [
        {
          provide: MentionsService,
          useValue: {
            searchTickets: vi.fn().mockReturnValue(of([])),
            searchProjects: vi.fn().mockReturnValue(of([])),
            searchFiles: vi.fn().mockReturnValue(of([])),
          },
        },
      ],
    });

    fixture = TestBed.createComponent(MentionEditor);
    fixture.componentRef.setInput('projectId', 'project-1');
    fixture.componentRef.setInput('projectName', 'TeamPilot');
    fixture.detectChanges();

    root = fixture.nativeElement.querySelector('[contenteditable]');
  }

  /** Types `text` at the end of the (assumed empty) editor and fires the same handler a real keystroke would. */
  function typeAtEnd(text: string): void {
    root.appendChild(document.createTextNode(text));
    setCaret(root.firstChild!, root.textContent!.length);
    fixture.componentInstance.onInput();
  }

  it('onInput_TypingAt_OpensTheMentionDropdown', () => {
    setUp();
    typeAtEnd('@');

    expect(fixture.componentInstance.mentionQuery()).toBe('');
  });

  it('onCategorySelected_Project_InsertsALiteralLabelIntoTheEditorText', () => {
    setUp();
    typeAtEnd('@');

    fixture.componentInstance.onCategorySelected('project');

    expect(getCanonicalText(root)).toBe('@project: ');
    expect(fixture.componentInstance.mentionQuery()).toBe('');
  });

  it('onCategorySelected_File_InsertsTheFileLabelDiscardingAnyTextTypedBeforeChoosingIt', () => {
    setUp();
    typeAtEnd('@bil'); // typed before a category was chosen - should be discarded, not kept as a prefix

    fixture.componentInstance.onCategorySelected('file');

    expect(getCanonicalText(root)).toBe('@file: ');
  });

  it('onInput_TypingAfterTheChosenCategoryLabel_UpdatesTheQueryFromRightAfterIt', () => {
    setUp();
    typeAtEnd('@');
    fixture.componentInstance.onCategorySelected('ticket');

    root.appendChild(document.createTextNode('fix login'));
    setCaret(root.lastChild!, 'fix login'.length);
    fixture.componentInstance.onInput();

    expect(getCanonicalText(root)).toBe('@ticket: fix login');
    expect(fixture.componentInstance.mentionQuery()).toBe('fix login');
  });

  it('onInput_CaretMovedBeforeTheChosenCategoryLabel_ClosesTheMentionDropdown', () => {
    setUp();
    typeAtEnd('@');
    fixture.componentInstance.onCategorySelected('project');
    root.appendChild(document.createTextNode('bil'));

    setCaret(root.firstChild!, 0); // caret back at the very start, before the "@project: " label
    fixture.componentInstance.onInput();

    expect(fixture.componentInstance.mentionQuery()).toBeNull();
  });
});
