import { Component, computed, effect, ElementRef, inject, input, output, signal, viewChild } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { LiveAgentChatService } from '../../../core/services/live-agent-chat.service';
import { NotificationService } from '../../../core/notification/notification.service';
import { AuthService } from '../../../core/services/auth.service';
import { ChatMessageDto, ConversationDto } from '../../../core/models';

@Component({
  selector: 'app-chat-panel',
  imports: [ReactiveFormsModule],
  templateUrl: './chat-panel.html',
})
export class ChatPanel {
  private readonly chatService = inject(LiveAgentChatService);
  private readonly notifications = inject(NotificationService);
  private readonly authService = inject(AuthService);
  private readonly fb = inject(FormBuilder);

  readonly projectId = input.required<string>();
  // The sprint whose board this chat panel is rendered alongside - used only to tell the server
  // which sprint a drafted-and-approved ticket should be created in (see approveTicket). The
  // conversation itself stays project-scoped (see ConversationDto.projectId): Live Agent chat
  // sessions aren't tied to one sprint, only where an approved draft lands is.
  readonly sprintId = input.required<string>();
  // Layout state (collapsed to a thin strip so the board can expand) lives in the parent Board,
  // not here - collapsing is a board-layout concern, and this component already takes its data
  // scope (projectId) as an input the same way.
  readonly collapsed = input(false);
  readonly collapsedChange = output<boolean>();

  readonly conversations = signal<ConversationDto[]>([]);
  readonly loadingConversations = signal(true);
  readonly creatingConversation = signal(false);
  readonly selectedConversationId = signal<string | null>(null);
  readonly selectedConversation = computed(() =>
    this.conversations().find((c) => c.id === this.selectedConversationId())
  );

  readonly renaming = signal(false);
  readonly savingRename = signal(false);
  readonly renameForm = this.fb.nonNullable.group({
    title: ['', Validators.required],
  });

  // Only one proposed-ticket draft can be edited at a time, same pattern as conversation
  // renaming above - edits are local to the form until "Create ticket" is clicked.
  readonly ticketEditingMessageId = signal<string | null>(null);
  readonly ticketEditForm = this.fb.nonNullable.group({
    title: ['', Validators.required],
    description: [''],
    acceptanceCriteria: ['', Validators.required],
  });
  // Grows the edit form's description textarea to fit whatever's already in it (the model's
  // draft can be much longer than its fixed rows="3"), keyed off ticketEditingMessageId so it
  // re-measures every time a different message's edit form mounts.
  private readonly ticketDescriptionTextarea = viewChild<ElementRef<HTMLTextAreaElement>>('ticketDescriptionTextarea');

  readonly messages = signal<ChatMessageDto[]>([]);
  readonly loading = signal(true);
  readonly sending = signal(false);
  // The decision itself is persisted server-side on the message (createdTicketId/ticketRejected)
  // so it survives a reload and is visible to every user - this only tracks an in-flight
  // approve/reject request so both buttons can be disabled while it's outstanding.
  readonly processingTicketMessageIds = signal<ReadonlySet<string>>(new Set());

  readonly form = this.fb.nonNullable.group({
    content: ['', Validators.required],
  });

  constructor() {
    effect(() => {
      const projectId = this.projectId();
      this.loadConversations(projectId);
    });

    effect(() => {
      this.ticketEditingMessageId();
      const textarea = this.ticketDescriptionTextarea()?.nativeElement;
      if (textarea) {
        this.resizeTicketDescriptionTextarea(textarea);
      }
    });
  }

  toggleCollapsed(): void {
    this.collapsedChange.emit(!this.collapsed());
  }

  private loadConversations(projectId: string): void {
    this.loadingConversations.set(true);
    this.chatService.listConversations(projectId).subscribe({
      next: (conversations) => {
        this.conversations.set(conversations);
        this.loadingConversations.set(false);

        const currentId = this.selectedConversationId();
        const stillExists = currentId !== null && conversations.some((c) => c.id === currentId);
        if (!stillExists) {
          this.selectConversation(conversations[0]?.id ?? null);
        }
      },
      error: () => this.loadingConversations.set(false),
    });
  }

  selectConversation(conversationId: string | null): void {
    this.cancelRename();
    this.selectedConversationId.set(conversationId);

    if (!conversationId) {
      this.messages.set([]);
      this.loading.set(false);
      return;
    }

    this.loading.set(true);
    this.chatService.listMessages(this.projectId(), conversationId).subscribe({
      next: (messages) => {
        this.messages.set(messages);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  onConversationSelected(conversationId: string): void {
    if (conversationId !== this.selectedConversationId()) {
      this.selectConversation(conversationId);
    }
  }

  createConversation(): void {
    if (this.creatingConversation()) {
      return;
    }

    this.creatingConversation.set(true);
    this.chatService.createConversation(this.projectId(), { title: null }).subscribe({
      next: (conversation) => {
        this.conversations.update((list) => [conversation, ...list]);
        this.creatingConversation.set(false);
        this.selectConversation(conversation.id);
      },
      error: () => {
        this.creatingConversation.set(false);
        this.notifications.error('Could not start a new chat session.');
      },
    });
  }

  startRename(): void {
    const conversation = this.selectedConversation();
    if (!conversation) {
      return;
    }

    this.renameForm.setValue({ title: conversation.title });
    this.renaming.set(true);
  }

  cancelRename(): void {
    this.renaming.set(false);
  }

  saveRename(): void {
    const conversation = this.selectedConversation();
    if (!conversation || this.renameForm.invalid || this.savingRename()) {
      this.renameForm.markAllAsTouched();
      return;
    }

    const title = this.renameForm.getRawValue().title.trim();
    if (!title) {
      return;
    }

    if (title === conversation.title) {
      this.renaming.set(false);
      return;
    }

    this.savingRename.set(true);
    this.chatService.renameConversation(this.projectId(), conversation.id, { title }).subscribe({
      next: (updated) => {
        this.conversations.update((list) => list.map((c) => (c.id === updated.id ? updated : c)));
        this.savingRename.set(false);
        this.renaming.set(false);
      },
      error: () => {
        this.savingRename.set(false);
        this.notifications.error('Could not rename this chat session.');
      },
    });
  }

  send(): void {
    const conversationId = this.selectedConversationId();
    if (this.form.invalid || this.sending() || !conversationId) {
      this.form.markAllAsTouched();
      return;
    }

    const content = this.form.getRawValue().content.trim();
    if (!content) {
      return;
    }

    this.sending.set(true);
    this.messages.update((messages) => [
      ...messages,
      {
        id: `pending-${Date.now()}`,
        role: 'User',
        content,
        proposedTicketTitle: null,
        proposedTicketDescription: null,
        proposedTicketAcceptanceCriteria: null,
        createdTicketId: null,
        ticketRejected: false,
        senderName: this.authService.currentUser()?.name ?? null,
        createdAtUtc: new Date().toISOString(),
      },
    ]);
    this.form.reset({ content: '' });

    this.chatService.sendMessage(this.projectId(), conversationId, { content }).subscribe({
      next: (reply) => {
        this.messages.update((messages) => [...messages, reply]);
        this.sending.set(false);
      },
      error: () => this.sending.set(false),
    });
  }

  startEditTicket(message: ChatMessageDto): void {
    if (this.isTicketDecided(message)) {
      return;
    }

    this.ticketEditForm.setValue({
      title: message.proposedTicketTitle ?? '',
      description: message.proposedTicketDescription ?? '',
      acceptanceCriteria: message.proposedTicketAcceptanceCriteria ?? '',
    });
    this.ticketEditingMessageId.set(message.id);
  }

  cancelEditTicket(): void {
    this.ticketEditingMessageId.set(null);
  }

  isEditingTicket(message: ChatMessageDto): boolean {
    return this.ticketEditingMessageId() === message.id;
  }

  onTicketDescriptionInput(event: Event): void {
    this.resizeTicketDescriptionTextarea(event.target as HTMLTextAreaElement);
  }

  private resizeTicketDescriptionTextarea(textarea: HTMLTextAreaElement): void {
    textarea.style.height = 'auto';
    textarea.style.height = `${textarea.scrollHeight}px`;
  }

  approveTicket(message: ChatMessageDto): void {
    const conversationId = this.selectedConversationId();
    if (!conversationId || !message.proposedTicketTitle || this.isTicketDecided(message) || this.isProcessingTicket(message)) {
      return;
    }

    const isEditing = this.isEditingTicket(message);
    if (isEditing && this.ticketEditForm.invalid) {
      this.ticketEditForm.markAllAsTouched();
      return;
    }

    const title = (isEditing ? this.ticketEditForm.getRawValue().title : message.proposedTicketTitle).trim();
    const description = (
      isEditing ? this.ticketEditForm.getRawValue().description : message.proposedTicketDescription ?? ''
    ).trim();
    const acceptanceCriteria = (
      isEditing ? this.ticketEditForm.getRawValue().acceptanceCriteria : message.proposedTicketAcceptanceCriteria ?? ''
    ).trim();
    if (!title || !acceptanceCriteria) {
      return;
    }

    this.processingTicketMessageIds.update((ids) => new Set(ids).add(message.id));
    this.chatService
      .approveTicket(this.projectId(), conversationId, message.id, {
        sprintId: this.sprintId(),
        title,
        description: description || null,
        acceptanceCriteria,
      })
      .subscribe({
        next: (updated) => {
          this.notifications.success('Ticket created.');
          this.messages.update((messages) => messages.map((m) => (m.id === updated.id ? updated : m)));
          this.removeProcessingId(message.id);
          this.clearEditingIfMatches(message.id);
        },
        error: () => this.removeProcessingId(message.id),
      });
  }

  rejectTicket(message: ChatMessageDto): void {
    const conversationId = this.selectedConversationId();
    if (!conversationId || !message.proposedTicketTitle || this.isTicketDecided(message) || this.isProcessingTicket(message)) {
      return;
    }

    this.processingTicketMessageIds.update((ids) => new Set(ids).add(message.id));
    this.chatService.rejectTicket(this.projectId(), conversationId, message.id).subscribe({
      next: (updated) => {
        this.messages.update((messages) => messages.map((m) => (m.id === updated.id ? updated : m)));
        this.removeProcessingId(message.id);
        this.clearEditingIfMatches(message.id);
      },
      error: () => this.removeProcessingId(message.id),
    });
  }

  isTicketCreated(message: ChatMessageDto): boolean {
    return message.createdTicketId !== null;
  }

  isTicketRejected(message: ChatMessageDto): boolean {
    return message.ticketRejected;
  }

  isTicketDecided(message: ChatMessageDto): boolean {
    return this.isTicketCreated(message) || this.isTicketRejected(message);
  }

  isProcessingTicket(message: ChatMessageDto): boolean {
    return this.processingTicketMessageIds().has(message.id);
  }

  private removeProcessingId(messageId: string): void {
    this.processingTicketMessageIds.update((ids) => {
      const next = new Set(ids);
      next.delete(messageId);
      return next;
    });
  }

  private clearEditingIfMatches(messageId: string): void {
    if (this.ticketEditingMessageId() === messageId) {
      this.ticketEditingMessageId.set(null);
    }
  }
}
