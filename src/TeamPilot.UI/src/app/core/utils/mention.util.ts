export type MentionType = 'ticket' | 'project' | 'file';

// Mirrors the backend's MentionParser regex (TeamPilot.Application/Tickets/Mentions/MentionParser.cs)
// - kept in sync manually, there's no shared codegen between the .NET and Angular projects. A
// file mention's "id" is the id of the PROJECT it belongs to (there's no other way to
// disambiguate a path once more than one project's files can be referenced), with the path
// itself carried in an extra trailing ":<path>" group only file tokens have.
const MENTION_PATTERN =
  /@\[([^\]\[]+)\]\((ticket|project|file):([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})(?::([^)]+))?\)/g;

export interface MentionTrigger {
  query: string;
  triggerStart: number;
}

/**
 * Scans backward from the caret for an unescaped "@" with no whitespace/newline between it and
 * the caret, and not itself preceded by a non-whitespace character (so "a@b" isn't treated as a
 * mention trigger). Returns null when the caret isn't currently inside a mention's "@query" text.
 */
export function findMentionTrigger(text: string, caretIndex: number): MentionTrigger | null {
  let i = caretIndex - 1;
  while (i >= 0) {
    const ch = text[i];
    if (ch === '@') {
      const prev = i > 0 ? text[i - 1] : undefined;
      if (prev !== undefined && !/\s/.test(prev)) {
        return null;
      }
      return { query: text.slice(i + 1, caretIndex), triggerStart: i };
    }
    if (/\s/.test(ch)) {
      return null;
    }
    i--;
  }
  return null;
}

export interface MentionSegment {
  type: 'text' | 'mention';
  text: string;
  mentionType?: MentionType;
  mentionId?: string;
  /** Only set when `mentionType === 'file'` - the path within the project named by `mentionId`. */
  filePath?: string;
}

/** Splits raw description text into plain-text and mention segments, for chip rendering (see MentionText). */
export function parseMentionSegments(text: string): MentionSegment[] {
  const segments: MentionSegment[] = [];
  let lastIndex = 0;

  for (const match of text.matchAll(MENTION_PATTERN)) {
    const index = match.index ?? 0;
    if (index > lastIndex) {
      segments.push({ type: 'text', text: text.slice(lastIndex, index) });
    }
    segments.push({
      type: 'mention',
      text: match[1],
      mentionType: match[2] as MentionType,
      mentionId: match[3],
      filePath: match[4],
    });
    lastIndex = index + match[0].length;
  }

  if (lastIndex < text.length) {
    segments.push({ type: 'text', text: text.slice(lastIndex) });
  }

  return segments;
}

/** Display label matching how a mention should read wherever it's shown: "@file: src/foo.ts", "@ticket: Fix login bug". */
export function mentionDisplayLabel(type: MentionType, label: string): string {
  return `@${type}: ${label}`;
}
