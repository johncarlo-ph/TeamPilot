import { Component, computed, input } from '@angular/core';

type DiffLineKind = 'add' | 'remove' | 'hunk' | 'header' | 'context';

interface DiffLine {
  text: string;
  kind: DiffLineKind;
}

/**
 * Minimal self-built unified-diff renderer (no external diff library): colors +/-
 * lines, dims file headers, and highlights @@ hunk markers. Good enough for the
 * git-style patches this API returns (CommitDto.diffContent, GitFileDiff.patch,
 * ConflictDto.conflictingDiffContent) without pulling in an extra dependency.
 */
@Component({
  selector: 'app-diff-viewer',
  template: `
    @if (lines().length) {
      <div class="diff-viewer p-2 rounded-2 mb-0">
        @for (line of lines(); track $index) {
          <div [class]="lineClass(line.kind)">{{ line.text }}</div>
        }
      </div>
    } @else {
      <p class="text-body-secondary fst-italic mb-0">No diff content available.</p>
    }
  `,
  styles: `
    .diff-viewer {
      background-color: #0d1117;
      color: #c9d1d9;
      font-family: 'Cascadia Code', 'Consolas', monospace;
      font-size: 0.825rem;
      overflow-x: auto;
      max-height: 32rem;
      overflow-y: auto;
    }
    .diff-line-add {
      background-color: rgba(46, 160, 67, 0.2);
      color: #7ee787;
      display: block;
      white-space: pre;
    }
    .diff-line-remove {
      background-color: rgba(248, 81, 73, 0.15);
      color: #ff7b72;
      display: block;
      white-space: pre;
    }
    .diff-line-hunk {
      color: #79c0ff;
      display: block;
      white-space: pre;
    }
    .diff-line-header {
      color: #8b949e;
      display: block;
      white-space: pre;
    }
    .diff-line-context {
      display: block;
      white-space: pre;
    }
  `,
})
export class DiffViewer {
  readonly content = input<string | null>(null);

  readonly lines = computed<DiffLine[]>(() => {
    const raw = this.content();
    if (!raw) {
      return [];
    }
    return raw.split('\n').map((text) => ({ text, kind: classify(text) }));
  });

  lineClass(kind: DiffLineKind): string {
    return `diff-line-${kind}`;
  }
}

function classify(line: string): DiffLineKind {
  if (line.startsWith('@@')) {
    return 'hunk';
  }
  if (
    line.startsWith('diff --git') ||
    line.startsWith('index ') ||
    line.startsWith('+++') ||
    line.startsWith('---')
  ) {
    return 'header';
  }
  if (line.startsWith('+')) {
    return 'add';
  }
  if (line.startsWith('-')) {
    return 'remove';
  }
  return 'context';
}
