import { Component, effect, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { LiveAgentChatService } from '../../../core/services/live-agent-chat.service';
import { TicketsService } from '../../../core/services/tickets.service';
import { NotificationService } from '../../../core/notification/notification.service';
import { ChatMessageDto } from '../../../core/models';

@Component({
  selector: 'app-chat-panel',
  imports: [ReactiveFormsModule],
  templateUrl: './chat-panel.html',
})
export class ChatPanel {
  private readonly chatService = inject(LiveAgentChatService);
  private readonly ticketsService = inject(TicketsService);
  private readonly notifications = inject(NotificationService);
  private readonly fb = inject(FormBuilder);

  readonly projectId = input.required<string>();

  readonly messages = signal<ChatMessageDto[]>([]);
  readonly loading = signal(true);
  readonly sending = signal(false);
  readonly createdTicketMessageIds = signal<ReadonlySet<string>>(new Set());
  readonly creatingTicketMessageIds = signal<ReadonlySet<string>>(new Set());

  readonly form = this.fb.nonNullable.group({
    content: ['', Validators.required],
  });

  constructor() {
    effect(() => {
      const projectId = this.projectId();
      this.loading.set(true);
      this.chatService.listMessages(projectId).subscribe({
        next: (messages) => {
          this.messages.set(messages);
          this.loading.set(false);
        },
        error: () => this.loading.set(false),
      });
    });
  }

  send(): void {
    if (this.form.invalid || this.sending()) {
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
        createdAtUtc: new Date().toISOString(),
      },
    ]);
    this.form.reset({ content: '' });

    this.chatService.sendMessage(this.projectId(), { content }).subscribe({
      next: (reply) => {
        this.messages.update((messages) => [...messages, reply]);
        this.sending.set(false);
      },
      error: () => this.sending.set(false),
    });
  }

  approveTicket(message: ChatMessageDto): void {
    if (!message.proposedTicketTitle || this.isTicketCreated(message) || this.isCreatingTicket(message)) {
      return;
    }

    this.creatingTicketMessageIds.update((ids) => new Set(ids).add(message.id));
    this.ticketsService
      .create(this.projectId(), {
        title: message.proposedTicketTitle,
        description: message.proposedTicketDescription,
      })
      .subscribe({
        next: () => {
          this.notifications.success('Ticket created.');
          this.createdTicketMessageIds.update((ids) => new Set(ids).add(message.id));
          this.removeCreatingId(message.id);
        },
        error: () => this.removeCreatingId(message.id),
      });
  }

  isTicketCreated(message: ChatMessageDto): boolean {
    return this.createdTicketMessageIds().has(message.id);
  }

  isCreatingTicket(message: ChatMessageDto): boolean {
    return this.creatingTicketMessageIds().has(message.id);
  }

  private removeCreatingId(messageId: string): void {
    this.creatingTicketMessageIds.update((ids) => {
      const next = new Set(ids);
      next.delete(messageId);
      return next;
    });
  }
}
