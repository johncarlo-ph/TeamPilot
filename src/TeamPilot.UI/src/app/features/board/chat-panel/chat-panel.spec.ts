import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { ChatPanel } from './chat-panel';
import { LiveAgentChatService } from '../../../core/services/live-agent-chat.service';
import { AuthService } from '../../../core/services/auth.service';
import { NotificationService } from '../../../core/notification/notification.service';
import { ChatMessageDto, ConversationDto } from '../../../core/models';

function conversation(overrides: Partial<ConversationDto> = {}): ConversationDto {
  return {
    id: 'conv-1',
    projectId: 'project-1',
    title: 'General',
    createdByUserId: 'user-1',
    createdByName: 'Jordan Lee',
    createdAtUtc: new Date().toISOString(),
    updatedAtUtc: null,
    ...overrides,
  };
}

function message(overrides: Partial<ChatMessageDto> = {}): ChatMessageDto {
  return {
    id: 'msg-1',
    role: 'Assistant',
    content: 'Hello',
    proposedTicketTitle: null,
    proposedTicketDescription: null,
    proposedTicketAcceptanceCriteria: null,
    createdTicketId: null,
    ticketRejected: false,
    senderName: null,
    createdAtUtc: new Date().toISOString(),
    ...overrides,
  };
}

describe('ChatPanel', () => {
  let fixture: ComponentFixture<ChatPanel>;
  let chatService: {
    listConversations: ReturnType<typeof vi.fn>;
    createConversation: ReturnType<typeof vi.fn>;
    renameConversation: ReturnType<typeof vi.fn>;
    listMessages: ReturnType<typeof vi.fn>;
    sendMessage: ReturnType<typeof vi.fn>;
    approveTicket: ReturnType<typeof vi.fn>;
    rejectTicket: ReturnType<typeof vi.fn>;
  };

  function setUp(conversations: ConversationDto[], messagesByConversation: Record<string, ChatMessageDto[]> = {}): void {
    chatService = {
      listConversations: vi.fn().mockReturnValue(of(conversations)),
      createConversation: vi.fn(),
      renameConversation: vi.fn(),
      listMessages: vi.fn((_projectId: string, conversationId: string) => of(messagesByConversation[conversationId] ?? [])),
      sendMessage: vi.fn(),
      approveTicket: vi.fn(),
      rejectTicket: vi.fn(),
    };

    TestBed.configureTestingModule({
      imports: [ChatPanel],
      providers: [
        { provide: LiveAgentChatService, useValue: chatService },
        { provide: AuthService, useValue: { currentUser: () => ({ name: 'Jordan Lee' }) } },
        { provide: NotificationService, useValue: { success: vi.fn(), error: vi.fn() } },
      ],
    });

    fixture = TestBed.createComponent(ChatPanel);
    fixture.componentRef.setInput('projectId', 'project-1');
    fixture.componentRef.setInput('sprintId', 'sprint-1');
    fixture.detectChanges();
  }

  it('init_ConversationsExist_AutoSelectsMostRecentAndLoadsItsMessages', () => {
    const conv = conversation({ id: 'conv-1' });
    setUp([conv], { 'conv-1': [message({ content: 'Hi there' })] });

    expect(fixture.componentInstance.selectedConversationId()).toBe('conv-1');
    expect(chatService.listMessages).toHaveBeenCalledWith('project-1', 'conv-1');
    expect(fixture.nativeElement.textContent).toContain('Hi there');
  });

  it('init_NoConversations_ShowsEmptyStateAndDisablesComposer', () => {
    setUp([]);

    expect(fixture.componentInstance.selectedConversationId()).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Start a new chat session');
    const sendButton = (fixture.nativeElement as HTMLElement).querySelector('button[type="submit"]') as HTMLButtonElement;
    expect(sendButton.disabled).toBe(true);
  });

  it('createConversation_Succeeds_AddsToListAndSelectsIt', () => {
    setUp([]);
    const created = conversation({ id: 'conv-new', title: 'New chat' });
    chatService.createConversation.mockReturnValue(of(created));
    chatService.listMessages.mockReturnValue(of([]));

    fixture.componentInstance.createConversation();

    expect(chatService.createConversation).toHaveBeenCalledWith('project-1', { title: null });
    expect(fixture.componentInstance.conversations()).toContainEqual(created);
    expect(fixture.componentInstance.selectedConversationId()).toBe('conv-new');
  });

  it('onConversationSelected_DifferentConversation_LoadsItsOwnMessages', () => {
    const first = conversation({ id: 'conv-1' });
    const second = conversation({ id: 'conv-2', title: 'Bug triage' });
    setUp([first, second], {
      'conv-1': [message({ id: 'm1', content: 'First conversation' })],
      'conv-2': [message({ id: 'm2', content: 'Second conversation' })],
    });

    fixture.componentInstance.onConversationSelected('conv-2');
    fixture.detectChanges();

    expect(fixture.componentInstance.selectedConversationId()).toBe('conv-2');
    expect(fixture.nativeElement.textContent).toContain('Second conversation');
    expect(fixture.nativeElement.textContent).not.toContain('First conversation');
  });

  it('saveRename_NewTitle_UpdatesConversationInList', () => {
    const conv = conversation({ id: 'conv-1', title: 'General' });
    setUp([conv], { 'conv-1': [] });
    const renamed = { ...conv, title: 'Bug triage' };
    chatService.renameConversation.mockReturnValue(of(renamed));

    fixture.componentInstance.startRename();
    fixture.componentInstance.renameForm.setValue({ title: 'Bug triage' });
    fixture.componentInstance.saveRename();

    expect(chatService.renameConversation).toHaveBeenCalledWith('project-1', 'conv-1', { title: 'Bug triage' });
    expect(fixture.componentInstance.conversations()[0].title).toBe('Bug triage');
    expect(fixture.componentInstance.renaming()).toBe(false);
  });

  it('send_ConversationSelected_SendsToTheSelectedConversation', () => {
    const conv = conversation({ id: 'conv-1' });
    setUp([conv], { 'conv-1': [] });
    chatService.sendMessage.mockReturnValue(of(message({ id: 'reply-1', content: 'Reply', role: 'Assistant' })));

    fixture.componentInstance.form.setValue({ content: 'What does this project do?' });
    fixture.componentInstance.send();

    expect(chatService.sendMessage).toHaveBeenCalledWith('project-1', 'conv-1', { content: 'What does this project do?' });
  });

  it('approveTicket_NotEditing_SendsTheProposedTitleAndDescriptionAsIs', () => {
    const conv = conversation({ id: 'conv-1' });
    const proposal = message({
      id: 'msg-1',
      proposedTicketTitle: 'Fix login bug',
      proposedTicketDescription: "Users can't sign in.",
      proposedTicketAcceptanceCriteria: 'User can sign in.',
    });
    setUp([conv], { 'conv-1': [proposal] });
    chatService.approveTicket.mockReturnValue(of({ ...proposal, createdTicketId: 'ticket-1' }));

    fixture.componentInstance.approveTicket(proposal);

    expect(chatService.approveTicket).toHaveBeenCalledWith('project-1', 'conv-1', 'msg-1', {
      sprintId: 'sprint-1',
      title: 'Fix login bug',
      description: "Users can't sign in.",
      acceptanceCriteria: 'User can sign in.',
    });
  });

  it('startEditTicket_ThenApprove_SendsTheEditedTitleAndDescription', () => {
    const conv = conversation({ id: 'conv-1' });
    const proposal = message({
      id: 'msg-1',
      proposedTicketTitle: 'Fix login bug',
      proposedTicketDescription: "Users can't sign in.",
      proposedTicketAcceptanceCriteria: 'User can sign in.',
    });
    setUp([conv], { 'conv-1': [proposal] });
    chatService.approveTicket.mockReturnValue(of({ ...proposal, createdTicketId: 'ticket-1' }));

    fixture.componentInstance.startEditTicket(proposal);
    fixture.componentInstance.ticketEditForm.setValue({
      title: 'Fix Google login bug',
      description: 'Edited description',
      acceptanceCriteria: 'Edited acceptance criteria',
    });
    fixture.componentInstance.approveTicket(proposal);

    expect(chatService.approveTicket).toHaveBeenCalledWith('project-1', 'conv-1', 'msg-1', {
      sprintId: 'sprint-1',
      title: 'Fix Google login bug',
      description: 'Edited description',
      acceptanceCriteria: 'Edited acceptance criteria',
    });
  });

  it('startEditTicket_DecidedMessage_DoesNothing', () => {
    const conv = conversation({ id: 'conv-1' });
    const decided = message({
      id: 'msg-1',
      proposedTicketTitle: 'Fix login bug',
      createdTicketId: 'ticket-1',
    });
    setUp([conv], { 'conv-1': [decided] });

    fixture.componentInstance.startEditTicket(decided);

    expect(fixture.componentInstance.isEditingTicket(decided)).toBe(false);
  });

  it('cancelEditTicket_StopsEditing', () => {
    const conv = conversation({ id: 'conv-1' });
    const proposal = message({ id: 'msg-1', proposedTicketTitle: 'Fix login bug' });
    setUp([conv], { 'conv-1': [proposal] });

    fixture.componentInstance.startEditTicket(proposal);
    expect(fixture.componentInstance.isEditingTicket(proposal)).toBe(true);

    fixture.componentInstance.cancelEditTicket();

    expect(fixture.componentInstance.isEditingTicket(proposal)).toBe(false);
  });
});
