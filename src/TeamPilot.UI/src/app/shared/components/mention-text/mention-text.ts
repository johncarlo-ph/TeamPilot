import { Component, computed, input } from '@angular/core';
import { MentionType, mentionDisplayLabel, parseMentionSegments } from '../../../core/utils/mention.util';

// Renders raw description text with @-mention tokens (e.g. "@[Fix login bug](ticket:<id>)")
// replaced by readable "@type: label" chips - never binds raw HTML, so a label can't inject markup.
@Component({
  selector: 'app-mention-text',
  templateUrl: './mention-text.html',
})
export class MentionText {
  readonly text = input<string>('');

  readonly segments = computed(() => parseMentionSegments(this.text()));

  displayLabel(mentionType: MentionType, label: string): string {
    return mentionDisplayLabel(mentionType, label);
  }
}
