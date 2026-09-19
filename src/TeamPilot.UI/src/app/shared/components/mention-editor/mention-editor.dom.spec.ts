import { describe, expect, it } from 'vitest';
import {
  createMentionChipElement,
  domPositionForCanonicalOffset,
  getCanonicalText,
  getCanonicalTextAndCaretOffset,
  insertPlainTextAtCaret,
  renderCanonicalTextIntoElement,
  replaceCanonicalRange,
} from './mention-editor.dom';

function editor(): HTMLElement {
  const div = document.createElement('div');
  document.body.appendChild(div);
  return div;
}

function setCaret(node: Node, offset: number): void {
  const range = document.createRange();
  range.setStart(node, offset);
  range.collapse(true);
  const selection = window.getSelection()!;
  selection.removeAllRanges();
  selection.addRange(range);
}

describe('renderCanonicalTextIntoElement / getCanonicalText', () => {
  it('renderCanonicalTextIntoElement_PlainText_RoundTripsUnchanged', () => {
    const root = editor();
    renderCanonicalTextIntoElement(root, 'Just plain text');
    expect(getCanonicalText(root)).toBe('Just plain text');
  });

  it('renderCanonicalTextIntoElement_TextWithMention_RendersAChipAndRoundTrips', () => {
    const root = editor();
    const token = '@[Fix login bug](ticket:11111111-1111-1111-1111-111111111111)';
    renderCanonicalTextIntoElement(root, `See ${token} please`);

    const chip = root.querySelector('.mention-chip');
    expect(chip?.textContent).toBe('@ticket: Fix login bug');
    expect(getCanonicalText(root)).toBe(`See ${token} please`);
  });

  it('renderCanonicalTextIntoElement_FileMention_RendersAChipCarryingTheProjectIdAndPathAndRoundTrips', () => {
    const root = editor();
    const token = '@[src/app/foo.ts](file:11111111-1111-1111-1111-111111111111:src/app/foo.ts)';
    renderCanonicalTextIntoElement(root, `See ${token} please`);

    const chip = root.querySelector('.mention-chip');
    expect(chip?.textContent).toBe('@file: src/app/foo.ts');
    expect(getCanonicalText(root)).toBe(`See ${token} please`);
  });
});

describe('getCanonicalTextAndCaretOffset', () => {
  it('getCanonicalTextAndCaretOffset_CaretMidwayThroughTextNode_ReturnsCorrectOffset', () => {
    const root = editor();
    renderCanonicalTextIntoElement(root, 'Depends on billing');
    setCaret(root.firstChild!, 'Depends on bil'.length);

    const { text, caretOffset } = getCanonicalTextAndCaretOffset(root);
    expect(text).toBe('Depends on billing');
    expect(caretOffset).toBe('Depends on bil'.length);
  });

  it('getCanonicalTextAndCaretOffset_CaretRightAfterAChip_ReturnsOffsetPastTheWholeToken', () => {
    const root = editor();
    const token = '@[Billing Service](project:22222222-2222-2222-2222-222222222222)';
    renderCanonicalTextIntoElement(root, token);
    // Caret positioned between the chip node and nothing else - container is root, offset 1.
    setCaret(root, 1);

    const { caretOffset } = getCanonicalTextAndCaretOffset(root);
    expect(caretOffset).toBe(token.length);
  });
});

describe('domPositionForCanonicalOffset + replaceCanonicalRange', () => {
  it('replaceCanonicalRange_ReplacesTriggerFragmentWithAChip', () => {
    const root = editor();
    renderCanonicalTextIntoElement(root, 'Depends on @billi');

    const chip = createMentionChipElement('project', 'abc-123', 'Billing Service');
    replaceCanonicalRange(root, 'Depends on '.length, 'Depends on @billi'.length, [chip, document.createTextNode(' ')]);

    expect(getCanonicalText(root)).toBe('Depends on @[Billing Service](project:abc-123) ');
  });

  it('replaceCanonicalRange_PreservesTextAfterTheReplacedRange', () => {
    const root = editor();
    renderCanonicalTextIntoElement(root, 'See @bil for details');

    const chip = createMentionChipElement('ticket', 'xyz-789', 'Fix login bug');
    replaceCanonicalRange(root, 'See '.length, 'See @bil'.length, [chip]);

    expect(getCanonicalText(root)).toBe('See @[Fix login bug](ticket:xyz-789) for details');
  });

  it('domPositionForCanonicalOffset_OffsetRightBeforeAChip_ResolvesToEndOfThePrecedingTextNode', () => {
    const root = editor();
    renderCanonicalTextIntoElement(root, 'a@[B](ticket:11111111-1111-1111-1111-111111111111)c');

    // Offset 1 is the boundary between "a" and the chip - both a valid Range endpoint at the end
    // of the preceding text node and an index-based position on root represent the same spot;
    // this implementation resolves it to the former.
    const position = domPositionForCanonicalOffset(root, 1);
    expect(position.node).toBe(root.childNodes[0]);
    expect(position.offset).toBe(1);
  });

  it('domPositionForCanonicalOffset_OffsetRightAfterAChip_ResolvesToRootIndexAfterIt', () => {
    const root = editor();
    renderCanonicalTextIntoElement(root, 'a@[B](ticket:11111111-1111-1111-1111-111111111111)c');
    const tokenLength = '@[B](ticket:11111111-1111-1111-1111-111111111111)'.length;

    const position = domPositionForCanonicalOffset(root, 1 + tokenLength);
    expect(position.node).toBe(root);
    expect(position.offset).toBe(2);
  });
});

describe('insertPlainTextAtCaret', () => {
  it('insertPlainTextAtCaret_InsertsNewlineAtCaretPosition', () => {
    const root = editor();
    renderCanonicalTextIntoElement(root, 'Line one Line two');
    setCaret(root.firstChild!, 'Line one'.length);

    insertPlainTextAtCaret(root, '\n');

    expect(getCanonicalText(root)).toBe('Line one\n Line two');
  });
});
