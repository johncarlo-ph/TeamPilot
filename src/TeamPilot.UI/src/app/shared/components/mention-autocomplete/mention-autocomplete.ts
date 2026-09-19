import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { Component, computed, effect, ElementRef, inject, input, output, signal, viewChildren } from '@angular/core';
import { combineLatest, debounceTime, distinctUntilChanged, forkJoin, map, of, switchMap } from 'rxjs';
import { MentionsService } from '../../../core/services/mentions.service';
import { MentionType } from '../../../core/utils/mention.util';

export interface MentionSelection {
  type: MentionType;
  id: string;
  label: string;
  /** Only set for type 'file' - the path within the project named by `id`. */
  filePath?: string;
}

interface MentionListItem extends MentionSelection {
  meta: string;
}

interface MentionCategory {
  type: MentionType;
  label: string;
  icon: string;
}

export interface MentionProjectCandidate {
  id: string;
  name: string;
}

const MENTION_CATEGORIES: MentionCategory[] = [
  { type: 'project', label: 'Project', icon: 'bi-folder' },
  { type: 'ticket', label: 'Ticket', icon: 'bi-card-checklist' },
  { type: 'file', label: 'File', icon: 'bi-file-earmark-code' },
];

const MIN_QUERY_LENGTH = 2;
const MAX_FILE_RESULTS = 10;
const DEBOUNCE_MS = 200;

// Two phases: type "@" and this first shows a fixed Project/Ticket/File category picker: type()
// query text at this point is deliberately ignored, since these 3 short generic words would
// almost never fuzzy-match a real search term anyway. Selecting one (click or arrow+Enter)
// switches to a live search scoped to just that category, using whatever's typed from then on.
@Component({
  selector: 'app-mention-autocomplete',
  templateUrl: './mention-autocomplete.html',
})
export class MentionAutocomplete {
  private readonly mentionsService = inject(MentionsService);

  readonly query = input<string>('');
  /** Candidate projects for File-category search - the current project plus any already directly Project-mentioned. */
  readonly projects = input<MentionProjectCandidate[]>([]);
  readonly selected = output<MentionSelection>();
  readonly closed = output<void>();
  /** Fired the moment a category is picked, before any search - lets the host editor insert a literal "@type: " label so the user can see what they picked. */
  readonly categorySelected = output<MentionType>();

  readonly categories = MENTION_CATEGORIES;
  readonly category = signal<MentionType | null>(null);

  readonly loading = signal(false);
  readonly results = signal<MentionListItem[]>([]);
  readonly highlightedIndex = signal(0);

  readonly activeItems = computed<readonly { label: string }[]>(() => (this.category() === null ? this.categories : this.results()));

  private readonly itemElements = viewChildren<ElementRef<HTMLElement>>('itemEl');

  constructor() {
    effect(() => {
      const index = this.highlightedIndex();
      this.itemElements()[index]?.nativeElement.scrollIntoView?.({ block: 'nearest' });
    });

    combineLatest([toObservable(this.query), toObservable(this.category)])
      .pipe(
        debounceTime(DEBOUNCE_MS),
        distinctUntilChanged(([prevQuery, prevCategory], [nextQuery, nextCategory]) => prevQuery === nextQuery && prevCategory === nextCategory),
        switchMap(([query, category]) => {
          if (category === null) {
            return of<MentionListItem[]>([]);
          }
          const trimmed = query.trim();
          if (trimmed.length < MIN_QUERY_LENGTH) {
            return of<MentionListItem[]>([]);
          }
          this.loading.set(true);
          return this.searchCategory(category, trimmed);
        }),
        takeUntilDestroyed()
      )
      .subscribe((results) => {
        this.loading.set(false);
        this.results.set(results);
        this.highlightedIndex.set(0);
      });
  }

  private searchCategory(category: MentionType, query: string) {
    if (category === 'project') {
      return this.mentionsService
        .searchProjects(query)
        .pipe(map((projects) => projects.map((p) => ({ type: 'project' as const, id: p.id, label: p.name, meta: 'Project' }))));
    }

    if (category === 'ticket') {
      return this.mentionsService
        .searchTickets(query)
        .pipe(map((tickets) => tickets.map((t) => ({ type: 'ticket' as const, id: t.id, label: t.title, meta: t.projectName }))));
    }

    const candidateProjects = this.projects();
    if (candidateProjects.length === 0) {
      return of<MentionListItem[]>([]);
    }
    return forkJoin(
      candidateProjects.map((project) =>
        this.mentionsService.searchFiles(project.id, query).pipe(
          map((files) =>
            files.map((f) => ({ type: 'file' as const, id: project.id, label: f.path, meta: project.name, filePath: f.path }))
          )
        )
      )
    ).pipe(map((resultsByProject) => resultsByProject.flat().slice(0, MAX_FILE_RESULTS)));
  }

  /** Called by the host editor's (keydown) handler while this dropdown is open. Returns true if the key was consumed. */
  handleKeydown(event: KeyboardEvent): boolean {
    const items = this.activeItems();

    if (event.key === 'ArrowDown') {
      event.preventDefault();
      this.highlightedIndex.update((i) => Math.min(i + 1, Math.max(items.length - 1, 0)));
      return true;
    }

    if (event.key === 'ArrowUp') {
      event.preventDefault();
      this.highlightedIndex.update((i) => Math.max(i - 1, 0));
      return true;
    }

    if (event.key === 'Enter') {
      if (items.length === 0) {
        return false;
      }
      event.preventDefault();
      this.chooseHighlighted();
      return true;
    }

    if (event.key === 'Escape') {
      event.preventDefault();
      this.closed.emit();
      return true;
    }

    return false;
  }

  private chooseHighlighted(): void {
    if (this.category() === null) {
      this.selectCategory(this.categories[this.highlightedIndex()].type);
    } else {
      this.selectItem(this.results()[this.highlightedIndex()]);
    }
  }

  selectCategory(type: MentionType): void {
    this.category.set(type);
    this.highlightedIndex.set(0);
    this.categorySelected.emit(type);
  }

  selectItem(item: MentionListItem): void {
    this.selected.emit(item);
  }
}
