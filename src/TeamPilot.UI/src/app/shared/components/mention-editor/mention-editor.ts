import { AfterViewInit, Component, ElementRef, computed, forwardRef, input, signal, viewChild } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import {
  MentionAutocomplete,
  MentionProjectCandidate,
  MentionSelection,
} from '../mention-autocomplete/mention-autocomplete';
import { MentionType, findMentionTrigger } from '../../../core/utils/mention.util';
import {
  createMentionChipElement,
  getCanonicalTextAndCaretOffset,
  insertPlainTextAtCaret,
  renderCanonicalTextIntoElement,
  replaceCanonicalRange,
} from './mention-editor.dom';

// A `formControlName`-compatible replacement for a plain <textarea> that renders "@"-mention
// tokens as inline chips as you type, instead of the raw @[Label](type:id) text - a plain
// <textarea> can only ever show that raw text, since it has no concept of inline formatting.
// Built on a flat-structured `contenteditable` div (see mention-editor.dom.ts) rather than a
// third-party rich-text library - no such dependency exists in this codebase, and the feature
// set needed here (plain text + atomic mention chips, no other formatting) is narrow enough to
// not warrant one.
@Component({
  selector: 'app-mention-editor',
  imports: [MentionAutocomplete],
  templateUrl: './mention-editor.html',
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => MentionEditor), multi: true }],
})
export class MentionEditor implements ControlValueAccessor, AfterViewInit {
  readonly editorId = input<string | null>(null, { alias: 'id' });

  /** The project this ticket is being created in - always an eligible File-search target, and
   * the seed of `allKnownProjects` (see below). */
  readonly projectId = input.required<string>();
  readonly projectName = input.required<string>();

  private readonly editorRoot = viewChild.required<ElementRef<HTMLElement>>('editorRoot');
  private readonly mentionAutocomplete = viewChild(MentionAutocomplete);

  readonly mentionQuery = signal<string | null>(null);
  private mentionTriggerStart: number | null = null;

  // Set once a category is picked (see onCategorySelected) - the offset right after the literal
  // "@type: " label text inserted into the editor at that point, so onInput can compute the live
  // search query as everything typed after it instead of re-scanning for "@" (which would break
  // the moment that label's own space character enters the text).
  private mentionLabelEnd: number | null = null;

  // Grows as the user "@project"-mentions other projects while composing - a cross-project
  // File mention is only offered for (and, server-side, only accepted for) a project that's
  // already been directly Project-mentioned this way. See docs/frontend.md.
  private readonly extraKnownProjects = signal<MentionProjectCandidate[]>([]);

  readonly allKnownProjects = computed<MentionProjectCandidate[]>(() => {
    const seen = new Map<string, MentionProjectCandidate>();
    seen.set(this.projectId(), { id: this.projectId(), name: this.projectName() });
    for (const project of this.extraKnownProjects()) {
      seen.set(project.id, project);
    }
    return [...seen.values()];
  });

  private onChange: (value: string) => void = () => {};
  private onTouched: () => void = () => {};
  private lastWrittenValue = '';
  private viewReady = false;

  ngAfterViewInit(): void {
    this.viewReady = true;
    this.renderValue();
  }

  writeValue(value: string | null): void {
    this.lastWrittenValue = value ?? '';
    if (this.viewReady) {
      this.renderValue();
    }
  }

  registerOnChange(fn: (value: string) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.editorRoot().nativeElement.contentEditable = (!isDisabled).toString();
  }

  private renderValue(): void {
    renderCanonicalTextIntoElement(this.editorRoot().nativeElement, this.lastWrittenValue);
    this.extraKnownProjects.set([]);
    this.closeMentionAutocomplete();
  }

  onInput(): void {
    const { text, caretOffset } = getCanonicalTextAndCaretOffset(this.editorRoot().nativeElement);
    this.onChange(text);

    if (caretOffset === null) {
      this.closeMentionAutocomplete();
      return;
    }

    if (this.mentionLabelEnd !== null) {
      // A category was already picked - the caret moving back before its label (backspacing
      // through it, or clicking away) is what ends this attempt; everything typed at/after it is
      // the live search query, whitespace and all (unlike the "@" scan below, a query like
      // "fix login" is completely normal here).
      if (caretOffset < this.mentionLabelEnd) {
        this.closeMentionAutocomplete();
        return;
      }
      this.mentionQuery.set(text.slice(this.mentionLabelEnd, caretOffset));
      return;
    }

    const trigger = findMentionTrigger(text, caretOffset);
    if (trigger) {
      this.mentionTriggerStart = trigger.triggerStart;
      this.mentionQuery.set(trigger.query);
    } else {
      this.closeMentionAutocomplete();
    }
  }

  onKeydown(event: KeyboardEvent): void {
    if (this.mentionQuery() !== null && this.mentionAutocomplete()?.handleKeydown(event)) {
      return;
    }
    // Intercepted rather than left to the browser, which would otherwise insert a <div>/<br> and
    // break the flat text-node/chip structure the offset mapping in mention-editor.dom.ts relies on.
    if (event.key === 'Enter') {
      event.preventDefault();
      this.closeMentionAutocomplete();
      insertPlainTextAtCaret(this.editorRoot().nativeElement, '\n');
      this.onInput();
    }
  }

  onPaste(event: ClipboardEvent): void {
    event.preventDefault();
    const text = event.clipboardData?.getData('text/plain') ?? '';
    if (text) {
      this.closeMentionAutocomplete();
      insertPlainTextAtCaret(this.editorRoot().nativeElement, text);
      this.onInput();
    }
  }

  onBlur(): void {
    this.onTouched();
    this.closeMentionAutocomplete();
  }

  /** A category (Project/Ticket/File) was just picked from the dropdown's first phase - replaces
   * whatever was typed after "@" so far with a literal "@type: " label, so it's clear which
   * category is active while the user keeps typing to search within it. */
  onCategorySelected(type: MentionType): void {
    if (this.mentionTriggerStart === null) {
      return;
    }
    const root = this.editorRoot().nativeElement;
    const { caretOffset } = getCanonicalTextAndCaretOffset(root);
    if (caretOffset === null) {
      return;
    }

    root.focus();
    const label = `@${type}: `;
    replaceCanonicalRange(root, this.mentionTriggerStart, caretOffset, [document.createTextNode(label)]);
    this.mentionLabelEnd = this.mentionTriggerStart + label.length;
    this.mentionQuery.set('');
  }

  onMentionSelected(selection: MentionSelection): void {
    if (this.mentionTriggerStart === null) {
      return;
    }
    const root = this.editorRoot().nativeElement;
    const { caretOffset } = getCanonicalTextAndCaretOffset(root);
    if (caretOffset === null) {
      return;
    }

    if (selection.type === 'project') {
      this.extraKnownProjects.update((projects) =>
        projects.some((p) => p.id === selection.id) ? projects : [...projects, { id: selection.id, name: selection.label }]
      );
    }

    root.focus();
    const chip = createMentionChipElement(selection.type, selection.id, selection.label, selection.filePath);
    replaceCanonicalRange(root, this.mentionTriggerStart, caretOffset, [chip, document.createTextNode(' ')]);
    this.closeMentionAutocomplete();
    this.onInput();
  }

  closeMentionAutocomplete(): void {
    this.mentionQuery.set(null);
    this.mentionTriggerStart = null;
    this.mentionLabelEnd = null;
  }
}
