import { describe, expect, it } from 'vitest';
import { findMentionTrigger, parseMentionSegments } from './mention.util';

describe('findMentionTrigger', () => {
  it('findMentionTrigger_CaretRightAfterAt_ReturnsEmptyQuery', () => {
    const trigger = findMentionTrigger('Depends on @', 12);
    expect(trigger).toEqual({ query: '', triggerStart: 11 });
  });

  it('findMentionTrigger_CaretMidwayThroughQuery_ReturnsTypedTextSoFar', () => {
    const trigger = findMentionTrigger('Depends on @billi', 18);
    expect(trigger).toEqual({ query: 'billi', triggerStart: 11 });
  });

  it('findMentionTrigger_NoAtBeforeCaret_ReturnsNull', () => {
    expect(findMentionTrigger('Just plain text', 10)).toBeNull();
  });

  it('findMentionTrigger_WhitespaceBetweenAtAndCaret_ReturnsNull', () => {
    expect(findMentionTrigger('Email me @ noon', 11)).toBeNull();
  });

  it('findMentionTrigger_AtPrecededByNonWhitespace_ReturnsNull', () => {
    expect(findMentionTrigger('user@example', 5)).toBeNull();
  });

  it('findMentionTrigger_AtAtStartOfText_ReturnsTypedTextSoFar', () => {
    expect(findMentionTrigger('@bill', 5)).toEqual({ query: 'bill', triggerStart: 0 });
  });
});

describe('parseMentionSegments', () => {
  it('parseMentionSegments_NoMentions_ReturnsSingleTextSegment', () => {
    expect(parseMentionSegments('Just plain text')).toEqual([{ type: 'text', text: 'Just plain text' }]);
  });

  it('parseMentionSegments_SingleMention_SplitsAroundIt', () => {
    const segments = parseMentionSegments('See @[Fix login bug](ticket:11111111-1111-1111-1111-111111111111) for context.');

    expect(segments).toEqual([
      { type: 'text', text: 'See ' },
      { type: 'mention', text: 'Fix login bug', mentionType: 'ticket', mentionId: '11111111-1111-1111-1111-111111111111' },
      { type: 'text', text: ' for context.' },
    ]);
  });

  it('parseMentionSegments_MentionAtStartAndEnd_HasNoEmptyTextSegments', () => {
    const segments = parseMentionSegments(
      '@[A](project:11111111-1111-1111-1111-111111111111)@[B](ticket:22222222-2222-2222-2222-222222222222)'
    );

    expect(segments).toEqual([
      { type: 'mention', text: 'A', mentionType: 'project', mentionId: '11111111-1111-1111-1111-111111111111' },
      { type: 'mention', text: 'B', mentionType: 'ticket', mentionId: '22222222-2222-2222-2222-222222222222' },
    ]);
  });
});
