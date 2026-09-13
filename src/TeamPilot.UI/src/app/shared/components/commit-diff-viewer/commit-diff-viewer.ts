import { Component, computed, input, signal } from '@angular/core';
import {
  DiffHunk,
  FileDiff,
  parseMultiFileDiff,
  toSplitRows,
} from '../../../core/utils/diff-parser.util';

type ViewMode = 'unified' | 'split';

/**
 * Renders a commit's full multi-file diff (`Commit.DiffContent`) as one collapsible section per
 * changed file, each with its own unified/split (before vs. after) toggle - so a multi-file
 * commit isn't one long unbroken wall of diff text.
 */
@Component({
  selector: 'app-commit-diff-viewer',
  template: `
    @if (!files().length) {
      <p class="text-body-secondary fst-italic mb-0">No diff content available.</p>
    } @else {
      @for (file of files(); track file.filePath) {
        <div class="commit-diff-file border rounded-2 mb-2">
          <button
            type="button"
            class="commit-diff-file-header btn w-100 d-flex align-items-center justify-content-between text-start"
            (click)="toggleFile(file.filePath)"
            [attr.aria-expanded]="isExpanded(file.filePath)"
          >
            <span class="d-flex align-items-center gap-2 text-truncate">
              <span class="commit-diff-caret">{{ isExpanded(file.filePath) ? '▾' : '▸' }}</span>
              <code class="text-truncate">{{ file.filePath }}</code>
              @if (file.isNew) {
                <span class="badge text-bg-success">new</span>
              }
              @if (file.isDeleted) {
                <span class="badge text-bg-danger">deleted</span>
              }
              @if (file.isRenamed) {
                <span class="badge text-bg-secondary">renamed</span>
              }
            </span>
            <span class="small text-nowrap ms-2">
              <span class="text-success">+{{ file.additions }}</span>
              <span class="text-danger ms-1">-{{ file.deletions }}</span>
            </span>
          </button>

          @if (isExpanded(file.filePath)) {
            <div class="commit-diff-file-body border-top">
              <div class="d-flex justify-content-end p-1 border-bottom">
                <div class="btn-group btn-group-sm" role="group" aria-label="Diff view mode">
                  <button
                    type="button"
                    class="btn btn-outline-secondary"
                    [class.active]="viewModeFor(file.filePath) === 'unified'"
                    (click)="setViewMode(file.filePath, 'unified')"
                  >
                    Unified
                  </button>
                  <button
                    type="button"
                    class="btn btn-outline-secondary"
                    [class.active]="viewModeFor(file.filePath) === 'split'"
                    (click)="setViewMode(file.filePath, 'split')"
                  >
                    Before/After
                  </button>
                </div>
              </div>

              @if (viewModeFor(file.filePath) === 'unified') {
                <div class="diff-viewer p-2 rounded-2 mb-0">
                  @for (hunk of file.hunks; track $index) {
                    <div class="diff-line-hunk">{{ hunk.header }}</div>
                    @for (line of hunk.lines; track $index) {
                      <div [class]="'diff-line-' + line.kind">{{ line.content }}</div>
                    }
                  }
                </div>
              } @else {
                <div class="diff-split rounded-2 mb-0">
                  @for (hunk of file.hunks; track $index) {
                    <div class="diff-line-hunk diff-split-row">
                      <span>{{ hunk.header }}</span>
                      <span>{{ hunk.header }}</span>
                    </div>
                    @for (row of splitRows(hunk); track $index) {
                      <div class="diff-split-row">
                        <span [class]="splitCellClass(row.left)">{{ row.left?.content ?? '' }}</span>
                        <span [class]="splitCellClass(row.right)">{{ row.right?.content ?? '' }}</span>
                      </div>
                    }
                  }
                </div>
              }
            </div>
          }
        </div>
      }
    }
  `,
  styles: `
    .commit-diff-file-header {
      background-color: transparent;
      border: none;
    }
    .commit-diff-caret {
      display: inline-block;
      width: 1rem;
      text-align: center;
      color: #8b949e;
    }
    .diff-viewer,
    .diff-split {
      background-color: #0d1117;
      color: #c9d1d9;
      font-family: 'Cascadia Code', 'Consolas', monospace;
      font-size: 0.825rem;
      overflow-x: auto;
      max-height: 32rem;
      overflow-y: auto;
    }
    .diff-split-row {
      display: grid;
      grid-template-columns: 1fr 1fr;
    }
    .diff-split-row + .diff-split-row {
      border-top: 1px solid rgba(240, 246, 252, 0.05);
    }
    .diff-split-row > span {
      display: block;
      white-space: pre;
      padding: 0 0.25rem;
      overflow-x: auto;
    }
    .diff-split-row > span:first-child {
      border-right: 1px solid rgba(240, 246, 252, 0.1);
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
    .diff-line-context {
      display: block;
      white-space: pre;
    }
    .diff-line-empty {
      display: block;
      white-space: pre;
      opacity: 0.4;
    }
  `,
})
export class CommitDiffViewer {
  readonly content = input<string | null>(null);

  readonly files = computed<FileDiff[]>(() => parseMultiFileDiff(this.content()));

  private readonly expandedFiles = signal<ReadonlySet<string>>(new Set());
  private readonly viewModes = signal<ReadonlyMap<string, ViewMode>>(new Map());

  isExpanded(filePath: string): boolean {
    return this.expandedFiles().has(filePath);
  }

  toggleFile(filePath: string): void {
    const next = new Set(this.expandedFiles());
    if (next.has(filePath)) {
      next.delete(filePath);
    } else {
      next.add(filePath);
    }
    this.expandedFiles.set(next);
  }

  viewModeFor(filePath: string): ViewMode {
    return this.viewModes().get(filePath) ?? 'unified';
  }

  setViewMode(filePath: string, mode: ViewMode): void {
    const next = new Map(this.viewModes());
    next.set(filePath, mode);
    this.viewModes.set(next);
  }

  splitRows(hunk: DiffHunk) {
    return toSplitRows(hunk);
  }

  splitCellClass(line: { kind: string } | null): string {
    return line ? `diff-line-${line.kind}` : 'diff-line-empty';
  }
}
