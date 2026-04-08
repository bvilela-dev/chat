import { CommonModule } from '@angular/common';
import { Component, DestroyRef, effect, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { combineLatest, firstValueFrom } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AuthService } from '../services/auth.service';
import { ChatQueryService } from '../services/chat-query.service';
import { ChatService } from '../services/chat.service';
import { ChatRealtimeMessage, ConversationReadDto } from '../models/chat.models';

@Component({
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  template: `
    <section class="layout" *ngIf="userName; else noSession">
      <aside class="sidebar">
        <div>
          <p class="eyebrow">Signed in</p>
          <h2>{{ userName }}</h2>
        </div>

        <button class="ghost" type="button" (click)="logout()">Logout</button>

        <div class="conversation-list">
          <button
            type="button"
            *ngFor="let conversation of conversations"
            [class.active]="conversation.id === activeConversationId"
            (click)="selectConversation(conversation.id)">
            <strong>{{ conversation.id | slice:0:8 }}</strong>
            <span>{{ conversation.lastMessage || 'No messages yet' }}</span>
          </button>
        </div>
      </aside>

      <main class="chat-panel">
        <header>
          <div>
            <p class="eyebrow">Conversation</p>
            <h1>{{ activeConversationId || 'Select a conversation' }}</h1>
          </div>
        </header>

        <div class="messages" *ngIf="activeConversationId; else emptyState">
          <article *ngFor="let message of messages" [class.me]="message.senderId === userId">
            <span>{{ message.senderName }}</span>
            <p>{{ message.content }}</p>
            <time>{{ message.createdAtUtc | date:'shortTime' }}</time>
          </article>
        </div>

        <ng-template #emptyState>
          <div class="empty">Choose a conversation on the left to load its query-side history and start sending SignalR commands.</div>
        </ng-template>

        <form class="composer" [formGroup]="form" (ngSubmit)="send()">
          <input type="text" formControlName="message" placeholder="Write a message">
          <button type="submit" [disabled]="form.invalid || !activeConversationId">Send</button>
        </form>
      </main>
    </section>

    <ng-template #noSession>
      <section class="empty-shell">
        <div class="empty-card">
          <p>You do not have an active session.</p>
          <button type="button" (click)="goToLogin()">Go to login</button>
        </div>
      </section>
    </ng-template>
  `,
  styles: [`
    .layout {
      min-height: 100vh;
      display: grid;
      grid-template-columns: 320px 1fr;
      gap: 1rem;
      padding: 1rem;
    }

    .sidebar,
    .chat-panel {
      background: var(--panel);
      border: 1px solid var(--panel-border);
      border-radius: 28px;
      box-shadow: var(--shadow);
      backdrop-filter: blur(20px);
    }

    .sidebar {
      padding: 1.2rem;
      display: grid;
      gap: 1rem;
      align-content: start;
    }

    .chat-panel {
      padding: 1.2rem;
      display: grid;
      grid-template-rows: auto 1fr auto;
      min-height: calc(100vh - 2rem);
    }

    .eyebrow {
      margin: 0;
      font-size: 0.72rem;
      letter-spacing: 0.16em;
      text-transform: uppercase;
      color: var(--accent-strong);
      font-family: 'Space Grotesk', sans-serif;
    }

    h1,
    h2 {
      margin: 0.3rem 0 0;
      font-family: 'Space Grotesk', sans-serif;
    }

    .conversation-list {
      display: grid;
      gap: 0.7rem;
      overflow: auto;
    }

    .conversation-list button,
    .ghost,
    .composer button {
      border: 0;
      cursor: pointer;
      border-radius: 18px;
    }

    .conversation-list button {
      text-align: left;
      display: grid;
      gap: 0.35rem;
      padding: 1rem;
      background: rgba(255, 255, 255, 0.68);
      color: var(--ink);
    }

    .conversation-list button.active {
      background: linear-gradient(135deg, rgba(14, 143, 106, 0.16), rgba(14, 143, 106, 0.08));
      border: 1px solid rgba(14, 143, 106, 0.3);
    }

    .conversation-list span {
      color: var(--muted);
      font-size: 0.92rem;
    }

    .ghost {
      background: rgba(35, 24, 13, 0.06);
      color: var(--ink);
      padding: 0.8rem 1rem;
    }

    .messages {
      overflow: auto;
      display: grid;
      gap: 0.8rem;
      padding: 1rem 0;
      align-content: start;
    }

    article {
      justify-self: start;
      max-width: min(70%, 620px);
      background: var(--bubble-other);
      border-radius: 22px 22px 22px 8px;
      padding: 0.9rem 1rem;
      display: grid;
      gap: 0.35rem;
    }

    article.me {
      justify-self: end;
      background: var(--bubble-me);
      border-radius: 22px 22px 8px 22px;
    }

    article span,
    time {
      color: var(--muted);
      font-size: 0.82rem;
    }

    article p {
      margin: 0;
      line-height: 1.45;
    }

    .composer {
      display: grid;
      grid-template-columns: 1fr auto;
      gap: 0.75rem;
      padding-top: 1rem;
    }

    .composer input {
      border: 1px solid rgba(35, 24, 13, 0.15);
      border-radius: 18px;
      padding: 1rem 1.1rem;
      background: rgba(255, 255, 255, 0.82);
    }

    .composer button,
    .empty-card button {
      background: linear-gradient(135deg, var(--accent), var(--accent-strong));
      color: white;
      padding: 0 1.4rem;
    }

    .empty,
    .empty-card {
      color: var(--muted);
      display: grid;
      place-items: center;
      text-align: center;
    }

    .empty-shell {
      min-height: 100vh;
      display: grid;
      place-items: center;
      padding: 2rem;
    }

    .empty-card {
      gap: 1rem;
      padding: 2rem;
      background: var(--panel);
      border: 1px solid var(--panel-border);
      border-radius: 24px;
      box-shadow: var(--shadow);
    }

    @media (max-width: 900px) {
      .layout {
        grid-template-columns: 1fr;
      }

      .chat-panel {
        min-height: auto;
      }

      article {
        max-width: 88%;
      }
    }
  `]
})
export class ChatComponent {
  private readonly authService = inject(AuthService);
  private readonly chatQueryService = inject(ChatQueryService);
  private readonly chatService = inject(ChatService);
  private readonly formBuilder = inject(FormBuilder);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  conversations: ConversationReadDto[] = [];
  messages: ChatRealtimeMessage[] = [];
  activeConversationId = '';
  userId = this.authService.user$.value?.id ?? '';
  userName = this.authService.user$.value?.name ?? '';

  readonly form = this.formBuilder.group({
    message: ['', [Validators.required]]
  });

  constructor() {
    const token = this.authService.getAccessToken();
    const user = this.authService.user$.value;

    if (!token || !user) {
      return;
    }

    this.userId = user.id;
    this.userName = user.name;

    void this.chatService.connect(token).then(async () => {
      this.conversations = await firstValueFrom(this.chatQueryService.getUserConversations(this.userId));
      if (this.conversations.length > 0) {
        await this.selectConversation(this.conversations[0].id);
      }
    });

    this.chatService.messages$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((messages) => {
        this.messages = messages.filter((message) => !this.activeConversationId || message.conversationId === this.activeConversationId);
      });

    effect(() => {
      const currentUser = this.authService.user$.value;
      if (!currentUser) {
        void this.router.navigate(['/login']);
      }
    });
  }

  async selectConversation(conversationId: string): Promise<void> {
    if (this.activeConversationId) {
      await this.chatService.leaveConversation(this.activeConversationId);
    }

    this.activeConversationId = conversationId;
    await this.chatService.joinConversation(conversationId);
    const history = await firstValueFrom(this.chatQueryService.getMessages(conversationId));
    this.chatService.replaceMessages(history);
  }

  async send(): Promise<void> {
    if (this.form.invalid || !this.activeConversationId) {
      return;
    }

    const message = this.form.getRawValue().message ?? '';
    await this.chatService.sendMessage(this.activeConversationId, message);
    this.form.reset();
  }

  logout(): void {
    this.authService.logout();
    void this.router.navigate(['/login']);
  }

  goToLogin(): void {
    void this.router.navigate(['/login']);
  }
}