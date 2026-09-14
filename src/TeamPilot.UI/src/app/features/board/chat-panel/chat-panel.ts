import { Component, computed, effect, inject, input, output, signal } from '@angular/core';
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

  approveTicket(message: ChatMessageDto): void {
    const conversationId = this.selectedConversationId();
    if (!conversationId || !message.proposedTicketTitle || this.isTicketDecided(message) || this.isProcessingTicket(message)) {
      return;
    }

    this.processingTicketMessageIds.update((ids) => new Set(ids).add(message.id));
    this.chatService.approveTicket(this.projectId(), conversationId, message.id).subscribe({
      next: (updated) => {
        this.notifications.success('Ticket created.');
        this.messages.update((messages) => messages.map((m) => (m.id === updated.id ? updated : m)));
        this.removeProcessingId(message.id);
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
}
