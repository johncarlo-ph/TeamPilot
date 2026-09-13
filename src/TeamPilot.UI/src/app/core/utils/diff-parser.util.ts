export type DiffLineKind = 'add' | 'remove' | 'context';

export interface DiffLine {
  kind: DiffLineKind;
  content: string;
}

export interface DiffHunk {
  header: string;
  lines: DiffLine[];
}

export interface FileDiff {
  filePath: string;
  oldPath: string | null;
  newPath: string | null;
  isNew: boolean;
  isDeleted: boolean;
  isRenamed: boolean;
  additions: number;
  deletions: number;
  hunks: DiffHunk[];
}

export interface SplitDiffRow {
  left: DiffLine | null;
  right: DiffLine | null;
}

const FILE_HEADER_PREFIX = 'diff --git ';
const FILE_HEADER_PATTERN = /^diff --git a\/(.+) b\/(.+)$/;

/**
 * Splits the multi-file unified-diff text the API returns for a whole commit
 * (`Commit.DiffContent`, built from LibGit2Sharp's `Patch.Content`, which concatenates one
 * "diff --git a/... b/..." block per changed file) into one parsed entry per file.
 */
export function parseMultiFileDiff(content: string | null | undefined): FileDiff[] {
  if (!content) {
    return [];
  }

  const lines = content.split('\n');
  const fileBlocks: string[][] = [];
  let current: string[] | null = null;

  for (const line of lines) {
    if (line.startsWith(FILE_HEADER_PREFIX)) {
      current = [line];
      fileBlocks.push(current);
    } else if (current) {
      current.push(line);
    }
  }

  return fileBlocks.map(parseFileBlock);
}

function parseFileBlock(blockLines: string[]): FileDiff {
  const headerMatch = blockLines[0]?.match(FILE_HEADER_PATTERN);
  let oldPath = headerMatch?.[1] ?? null;
  let newPath = headerMatch?.[2] ?? null;

  let isNew = false;
  let isDeleted = false;
  let isRenamed = false;
  let additions = 0;
  let deletions = 0;
  const hunks: DiffHunk[] = [];
  let currentHunk: DiffHunk | null = null;

  for (const line of blockLines.slice(1)) {
    if (line.startsWith('new file mode')) {
      isNew = true;
    } else if (line.startsWith('deleted file mode')) {
      isDeleted = true;
    } else if (line.startsWith('rename from ')) {
      isRenamed = true;
      oldPath = line.slice('rename from '.length);
    } else if (line.startsWith('rename to ')) {
      isRenamed = true;
      newPath = line.slice('rename to '.length);
    } else if (line.startsWith('--- ')) {
      oldPath = normalizePathHeader(line.slice(4), 'a/') ?? oldPath;
    } else if (line.startsWith('+++ ')) {
      newPath = normalizePathHeader(line.slice(4), 'b/') ?? newPath;
    } else if (line.startsWith('@@')) {
      currentHunk = { header: line, lines: [] };
      hunks.push(currentHunk);
    } else if (currentHunk && line.startsWith('\\ No newline at end of file')) {
      // Not a content line - ignore.
    } else if (currentHunk) {
      if (line.startsWith('+')) {
        currentHunk.lines.push({ kind: 'add', content: line.slice(1) });
        additions++;
      } else if (line.startsWith('-')) {
        currentHunk.lines.push({ kind: 'remove', content: line.slice(1) });
        deletions++;
      } else {
        currentHunk.lines.push({ kind: 'context', content: line.startsWith(' ') ? line.slice(1) : line });
      }
    }
  }

  return {
    filePath: newPath ?? oldPath ?? '(unknown file)',
    oldPath,
    newPath,
    isNew,
    isDeleted,
    isRenamed,
    additions,
    deletions,
    hunks,
  };
}

function normalizePathHeader(path: string, prefix: string): string | null {
  const trimmed = path.trim();
  if (trimmed === '/dev/null') {
    return null;
  }
  return trimmed.startsWith(prefix) ? trimmed.slice(prefix.length) : trimmed;
}

/**
 * Pairs up a hunk's removed/added lines into before/after rows for a side-by-side view.
 * Context lines map to themselves on both sides; consecutive removals and additions within
 * a change block are paired index-wise (extra lines on either side get a blank counterpart),
 * matching the split-diff convention GitHub/GitLab use.
 */
export function toSplitRows(hunk: DiffHunk): SplitDiffRow[] {
  const rows: SplitDiffRow[] = [];
  const lines = hunk.lines;
  let i = 0;

  while (i < lines.length) {
    const line = lines[i];
    if (line.kind === 'context') {
      rows.push({ left: line, right: line });
      i++;
      continue;
    }

    const removals: DiffLine[] = [];
    while (i < lines.length && lines[i].kind === 'remove') {
      removals.push(lines[i]);
      i++;
    }
    const additions: DiffLine[] = [];
    while (i < lines.length && lines[i].kind === 'add') {
      additions.push(lines[i]);
      i++;
    }

    const pairCount = Math.max(removals.length, additions.length);
    for (let j = 0; j < pairCount; j++) {
      rows.push({ left: removals[j] ?? null, right: additions[j] ?? null });
    }
  }

  return rows;
}
