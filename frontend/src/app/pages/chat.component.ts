import { CommonModule } from '@angular/common';
import { Component, DestroyRef, ElementRef, ViewChild, effect, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { firstValueFrom, timer } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AuthService } from '../services/auth.service';
import { ChatQueryService } from '../services/chat-query.service';
import { ChatService } from '../services/chat.service';
import { PresenceService } from '../services/presence.service';
import { UserDirectoryService } from '../services/user-directory.service';
import { ChatRealtimeMessage, ConversationReadDto, OnlineUser, UserDto } from '../models/chat.models';

interface ChatViewState {
  activeConversationId: string | null;
  activeContactId: string | null;
}

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

        <div class="section-header">
          <p class="eyebrow">Contacts</p>
        </div>

        <div class="people-list" *ngIf="contacts.length > 0; else noContacts">
          <button
            type="button"
            *ngFor="let contact of contacts"
            [class.active]="contact.id === activeContactId"
            [disabled]="startingConversationUserId === contact.id"
            (click)="startConversation(contact)">
            <div class="contact-row">
              <strong>{{ contact.name }}</strong>
              <span class="status-badge">
                <span class="status-dot" [class.online]="contact.isOnline" [class.offline]="!contact.isOnline"></span>
                {{ contact.isOnline ? 'Online' : 'Offline' }}
              </span>
            </div>
            <span>{{ contact.email }}</span>
          </button>
        </div>

        <ng-template #noContacts>
          <div class="empty-sidebar">No contacts available yet.</div>
        </ng-template>

      </aside>

      <main class="chat-panel">
        <header>
          <div>
            <p class="eyebrow">Conversation</p>
            <h1>{{ getActiveConversationTitle() }}</h1>
          </div>
        </header>

        <div #messagesContainer class="messages" *ngIf="activeConversationId; else emptyState">
          <article *ngFor="let message of messages" [class.me]="message.senderId === userId">
            <span>{{ message.senderName }}</span>
            <p>{{ message.content }}</p>
            <time>{{ message.createdAtUtc | date:'shortTime' }}</time>
          </article>
        </div>

        <ng-template #emptyState>
          <div class="empty">Select a contact on the left to load the conversation history and start chatting.</div>
        </ng-template>

        <form class="composer" *ngIf="activeConversationId" [formGroup]="form" (ngSubmit)="send()">
          <input type="text" formControlName="message" placeholder="Write a message">
          <button type="submit" [disabled]="form.invalid || !activeConversationId">Send</button>
        </form>
      </main>
    </section>

    <ng-template #noSession>
      <section class="empty-shell">
        <div class="empty-card">
          <p>You do not have an active session.</p>
          <button type="button" (click)="goToSignIn()">Go to sign in</button>
        </div>
      </section>
    </ng-template>
  `,
  styles: [`
    .layout {
      min-height: 100vh;
      display: grid;
      grid-template-columns: 320px minmax(0, 1fr);
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
      height: calc(100vh - 2rem);
      min-width: 0;
      min-height: 0;
      overflow: hidden;
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

    .people-list {
      display: grid;
      gap: 0.7rem;
      overflow: auto;
    }

    .people-list button,
    .ghost,
    .composer button {
      border: 0;
      cursor: pointer;
      border-radius: 18px;
    }

    .people-list button {
      text-align: left;
      display: grid;
      gap: 0.35rem;
      padding: 0.95rem 1rem;
      background: rgba(14, 143, 106, 0.09);
      color: var(--ink);
      border: 1px solid rgba(14, 143, 106, 0.18);
    }

    .contact-row {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 0.75rem;
    }

    .status-badge {
      display: inline-flex;
      align-items: center;
      gap: 0.4rem;
      font-size: 0.78rem;
      color: var(--muted);
      white-space: nowrap;
    }

    .status-dot {
      width: 0.65rem;
      height: 0.65rem;
      border-radius: 999px;
      display: inline-block;
      box-shadow: 0 0 0 3px rgba(255, 255, 255, 0.45);
    }

    .status-dot.online {
      background: #18a957;
    }

    .status-dot.offline {
      background: #d64545;
    }

    .people-list button:disabled {
      opacity: 0.65;
      cursor: progress;
    }

    .people-list button.active {
      background: linear-gradient(135deg, rgba(14, 143, 106, 0.16), rgba(14, 143, 106, 0.08));
      border: 1px solid rgba(14, 143, 106, 0.3);
    }

    .people-list span {
      color: var(--muted);
      font-size: 0.88rem;
    }

    .section-header {
      padding-top: 0.2rem;
    }

    .empty-sidebar {
      color: var(--muted);
      font-size: 0.92rem;
      padding: 0.2rem 0 0.6rem;
    }

    .ghost {
      background: rgba(35, 24, 13, 0.06);
      color: var(--ink);
      padding: 0.8rem 1rem;
    }

    .messages {
      overflow: auto;
      overflow-x: hidden;
      width: 100%;
      min-width: 0;
      min-height: 0;
      display: grid;
      gap: 0.8rem;
      padding: 1rem 0;
      align-content: start;
    }

    article {
      justify-self: start;
      width: fit-content;
      min-width: 0;
      max-width: min(70%, 620px);
      background: var(--bubble-other);
      border-radius: 22px 22px 22px 8px;
      padding: 0.9rem 1rem;
      display: grid;
      gap: 0.35rem;
      overflow-wrap: anywhere;
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

    article > * {
      max-width: 100%;
      min-width: 0;
    }

    article p {
      margin: 0;
      max-width: 100%;
      line-height: 1.45;
      white-space: pre-wrap;
      overflow-wrap: anywhere;
      word-break: break-word;
    }

    .composer {
      display: grid;
      grid-template-columns: 1fr auto;
      gap: 0.75rem;
      padding-top: 1rem;
      padding-bottom: 0.2rem;
      background: var(--panel);
      position: sticky;
      bottom: 0;
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
        height: calc(100vh - 2rem);
      }

      article {
        max-width: 88%;
      }
    }
  `]
})
export class ChatComponent {
  private readonly viewStateStorageKeyPrefix = 'chat.view-state';
  private readonly authService = inject(AuthService);
  private readonly chatQueryService = inject(ChatQueryService);
  private readonly chatService = inject(ChatService);
  private readonly presenceService = inject(PresenceService);
  private readonly userDirectoryService = inject(UserDirectoryService);
  private readonly formBuilder = inject(FormBuilder);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  @ViewChild('messagesContainer') private messagesContainer?: ElementRef<HTMLDivElement>;

  conversations: ConversationReadDto[] = [];
  messages: ChatRealtimeMessage[] = [];
  contacts: OnlineUser[] = [];
  private readonly usersById = new Map<string, UserDto>();
  activeConversationId = '';
  activeContactId = '';
  startingConversationUserId = '';
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

    void this.initializeAsync(token);

    this.chatService.messages$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((messages) => {
        this.messages = messages.filter((message) => !this.activeConversationId || message.conversationId === this.activeConversationId);
        this.scrollMessagesToBottom();
      });

    timer(0, 10000)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        void this.reloadDirectory();
      });

    this.destroyRef.onDestroy(() => {
      if (this.userId) {
        this.presenceService.setOffline(this.userId).subscribe({ error: () => undefined });
      }
    });

    effect(() => {
      const currentUser = this.authService.user$.value;
      if (!currentUser) {
        void this.router.navigate(['/signin']);
      }
    });
  }

  private async initializeAsync(token: string): Promise<void> {
    this.chatService.replaceMessages([]);
    await this.chatService.connect(token);
    await firstValueFrom(this.presenceService.setOnline(this.userId));
    await this.reloadConversations();
    await this.reloadDirectory();

    const persistedViewState = this.loadViewState();
    if (!persistedViewState?.activeConversationId) {
      this.clearActiveSelection(false);
      return;
    }

    const activeConversation = this.conversations.find((conversation) => conversation.id === persistedViewState.activeConversationId);
    if (!activeConversation) {
      this.clearActiveSelection();
      return;
    }

    this.activeContactId = persistedViewState.activeContactId
      ?? activeConversation.counterpartUserId
      ?? '';
    await this.selectConversation(activeConversation.id);
  }

  private async reloadConversations(): Promise<void> {
    this.conversations = await firstValueFrom(this.chatQueryService.getUserConversations(this.userId));
  }

  private async reloadDirectory(): Promise<void> {
    const directory = await firstValueFrom(this.userDirectoryService.getDirectory(this.userId));
    this.contacts = directory.contacts;
    this.usersById.clear();
    directory.users.forEach((user) => this.usersById.set(user.id, user));
  }

  async selectConversation(conversationId: string): Promise<void> {
    if (this.activeConversationId === conversationId) {
      return;
    }

    if (this.activeConversationId) {
      await this.chatService.leaveConversation(this.activeConversationId);
    }

    this.activeConversationId = conversationId;
  this.persistViewState();
    this.chatService.replaceMessages([]);
    await this.chatService.joinConversation(conversationId);
    const history = await firstValueFrom(this.chatQueryService.getMessages(conversationId));
    this.chatService.replaceMessages(history);
  }

  async startConversation(user: OnlineUser): Promise<void> {
    this.startingConversationUserId = user.id;

    try {
      if (this.activeContactId === user.id && this.activeConversationId) {
        return;
      }

      this.activeContactId = user.id;

      const conversation = await firstValueFrom(this.chatQueryService.createDirectConversation(user.id));
      await this.reloadConversations();

      await this.selectConversation(conversation.id);
    }
    finally {
      this.startingConversationUserId = '';
    }
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
    if (!this.userId) {
      this.authService.logout();
      void this.router.navigate(['/signin']);
      return;
    }

    this.presenceService.setOffline(this.userId).subscribe({
      next: () => this.finishLogout(),
      error: () => this.finishLogout()
    });
  }

  goToSignIn(): void {
    void this.router.navigate(['/signin']);
  }

  getConversationTitle(conversation: ConversationReadDto): string {
    if (conversation.isGroup) {
      return 'Group conversation';
    }

    if (!conversation.counterpartUserId) {
      return conversation.id;
    }

    return this.usersById.get(conversation.counterpartUserId)?.name ?? conversation.id;
  }

  getActiveConversationTitle(): string {
    if (this.activeContactId) {
      return this.usersById.get(this.activeContactId)?.name ?? 'Select a conversation';
    }

    const activeConversation = this.conversations.find((conversation) => conversation.id === this.activeConversationId);
    return activeConversation ? this.getConversationTitle(activeConversation) : 'Select a conversation';
  }

  private finishLogout(): void {
    void this.chatService.disconnect();
    this.clearViewState();
    this.authService.logout();
    void this.router.navigate(['/signin']);
  }

  private persistViewState(): void {
    if (!this.userId) {
      return;
    }

    const state: ChatViewState = {
      activeConversationId: this.activeConversationId || null,
      activeContactId: this.activeContactId || null
    };

    localStorage.setItem(this.getViewStateStorageKey(), JSON.stringify(state));
  }

  private loadViewState(): ChatViewState | null {
    if (!this.userId) {
      return null;
    }

    const rawState = localStorage.getItem(this.getViewStateStorageKey());
    return rawState ? JSON.parse(rawState) as ChatViewState : null;
  }

  private clearViewState(): void {
    if (!this.userId) {
      return;
    }

    localStorage.removeItem(this.getViewStateStorageKey());
  }

  private clearActiveSelection(persist = true): void {
    this.activeConversationId = '';
    this.activeContactId = '';
    this.chatService.replaceMessages([]);

    if (persist) {
      this.persistViewState();
    }
  }

  private getViewStateStorageKey(): string {
    return `${this.viewStateStorageKeyPrefix}.${this.userId}`;
  }

  private scrollMessagesToBottom(): void {
    const scheduleScroll = typeof requestAnimationFrame === 'function'
      ? requestAnimationFrame
      : (callback: FrameRequestCallback) => window.setTimeout(callback, 0);

    scheduleScroll(() => {
      const container = this.messagesContainer?.nativeElement;
      if (!container) {
        return;
      }

      container.scrollTop = container.scrollHeight;
    });
  }
}