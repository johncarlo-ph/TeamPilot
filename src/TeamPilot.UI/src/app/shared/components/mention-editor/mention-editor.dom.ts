import {
  MentionType,
  mentionDisplayLabel,
  parseMentionSegments,
} from '../../../core/utils/mention.util';

// DOM plumbing for MentionEditor's contenteditable surface. The editor's DOM is kept flat by
// construction (Enter is intercepted to insert a literal "\n" instead of letting the browser
// insert a <div>/<br>, and paste is intercepted to insert plain text only) - every direct child
// of the editor root is either a Text node or one atomic ("contenteditable=false") mention chip
// <span>, never a nested block element. That invariant is what makes the canonical-text/caret
// offset mapping below tractable; a stray non-text/non-chip node (e.g. a browser-inserted <br>
// in an emptied field) is simply skipped/treated as zero-width rather than crashing on it.

const CHIP_CLASS = 'mention-chip';

export function createMentionChipElement(
  type: MentionType,
  id: string,
  label: string,
  filePath?: string,
): HTMLElement {
  const span = document.createElement('span');
  span.className = `${CHIP_CLASS} badge rounded-pill text-bg-primary`;
  span.contentEditable = 'false';
  span.dataset['mentionType'] = type;
  span.dataset['mentionId'] = id;
  span.dataset['mentionLabel'] = label;
  if (filePath !== undefined) {
    span.dataset['mentionFilePath'] = filePath;
  }
  span.textContent = mentionDisplayLabel(type, label);
  return span;
}

export function isMentionChipElement(node: Node): node is HTMLElement {
  return (
    node.nodeType === Node.ELEMENT_NODE && (node as HTMLElement).classList.contains(CHIP_CLASS)
  );
}

function mentionTokenFromChipElement(el: HTMLElement): string {
  const type = el.dataset['mentionType'];
  const id = el.dataset['mentionId'];
  const label = el.dataset['mentionLabel'] ?? '';
  const filePath = el.dataset['mentionFilePath'];
  return filePath !== undefined
    ? `@[${label}](${type}:${id}:${filePath})`
    : `@[${label}](${type}:${id})`;
}

/** Clears `root` and repopulates it with text nodes / chip spans matching `text`'s mention tokens. */
export function renderCanonicalTextIntoElement(root: HTMLElement, text: string): void {
  root.textContent = '';
  for (const segment of parseMentionSegments(text)) {
    root.appendChild(
      segment.type === 'mention'
        ? createMentionChipElement(
            segment.mentionType!,
            segment.mentionId!,
            segment.text,
            segment.filePath,
          )
        : document.createTextNode(segment.text),
    );
  }
}

function childCanonicalLength(child: Node): number {
  if (child.nodeType === Node.TEXT_NODE) {
    return child.textContent?.length ?? 0;
  }
  if (isMentionChipElement(child)) {
    return mentionTokenFromChipElement(child).length;
  }
  return 0;
}

/** Reconstructs the canonical mention-token string `root` currently represents. */
export function getCanonicalText(root: HTMLElement): string {
  let text = '';
  for (const child of Array.from(root.childNodes)) {
    if (child.nodeType === Node.TEXT_NODE) {
      text += child.textContent ?? '';
    } else if (isMentionChipElement(child)) {
      text += mentionTokenFromChipElement(child);
    }
  }
  return text;
}

/**
 * Reconstructs the canonical text (see {@link getCanonicalText}) plus the caret's offset into
 * it, from the current selection. Returns a null `caretOffset` when the selection isn't inside
 * `root` at all (e.g. focus moved elsewhere).
 */
export function getCanonicalTextAndCaretOffset(root: HTMLElement): {
  text: string;
  caretOffset: number | null;
} {
  const selection = window.getSelection();
  const range = selection && selection.rangeCount > 0 ? selection.getRangeAt(0) : null;
  const caretNode = range && root.contains(range.startContainer) ? range.startContainer : null;

  let text = '';
  let caretOffset: number | null = null;
  const children = Array.from(root.childNodes);

  children.forEach((child, index) => {
    if (caretNode === root && range!.startOffset === index) {
      caretOffset = text.length;
    }
    if (child === caretNode && child.nodeType === Node.TEXT_NODE) {
      caretOffset = text.length + range!.startOffset;
    }
    text +=
      child.nodeType === Node.TEXT_NODE
        ? (child.textContent ?? '')
        : isMentionChipElement(child)
          ? mentionTokenFromChipElement(child)
          : '';
  });

  if (caretNode === root && range!.startOffset === children.length) {
    caretOffset = text.length;
  }

  return { text, caretOffset };
}

interface DomPosition {
  node: Node;
  offset: number;
}

/** Maps a canonical-text offset back to a DOM (node, offset) position usable with `Range`. */
export function domPositionForCanonicalOffset(
  root: HTMLElement,
  canonicalOffset: number,
): DomPosition {
  let consumed = 0;
  const children = Array.from(root.childNodes);

  for (let index = 0; index < children.length; index++) {
    const child = children[index];
    const length = childCanonicalLength(child);

    if (canonicalOffset <= consumed + length) {
      if (child.nodeType === Node.TEXT_NODE) {
        return { node: child, offset: canonicalOffset - consumed };
      }
      // A chip is atomic - an offset inside its token range snaps to just before or just after it.
      return { node: root, offset: canonicalOffset === consumed ? index : index + 1 };
    }
    consumed += length;
  }

  return { node: root, offset: children.length };
}

/** Deletes the canonical-text range `[startOffset, endOffset)` and inserts `nodes` in its place, leaving the caret right after the last inserted node. */
export function replaceCanonicalRange(
  root: HTMLElement,
  startOffset: number,
  endOffset: number,
  nodes: Node[],
): void {
  const start = domPositionForCanonicalOffset(root, startOffset);
  const end = domPositionForCanonicalOffset(root, endOffset);

  const range = document.createRange();
  range.setStart(start.node, start.offset);
  range.setEnd(end.node, end.offset);
  range.deleteContents();

  const fragment = document.createDocumentFragment();
  for (const node of nodes) {
    fragment.appendChild(node);
  }
  const lastNode = nodes[nodes.length - 1];
  range.insertNode(fragment);

  if (lastNode) {
    setCaretAfterNode(lastNode);
  }
}

/** Inserts `text` as a plain text node at the current caret position, replacing any active selection. */
export function insertPlainTextAtCaret(root: HTMLElement, text: string): void {
  const selection = window.getSelection();
  if (
    !selection ||
    selection.rangeCount === 0 ||
    !root.contains(selection.getRangeAt(0).startContainer)
  ) {
    return;
  }
  const range = selection.getRangeAt(0);
  range.deleteContents();
  const textNode = document.createTextNode(text);
  range.insertNode(textNode);
  setCaretAfterNode(textNode);
}

export function setCaretAfterNode(node: Node): void {
  const selection = window.getSelection();
  if (!selection) {
    return;
  }
  const range = document.createRange();
  range.setStartAfter(node);
  range.collapse(true);
  selection.removeAllRanges();
  selection.addRange(range);
}
