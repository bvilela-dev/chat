import { Injectable } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { BehaviorSubject } from 'rxjs';
import { ChatRealtimeMessage } from '../models/chat.models';

@Injectable({ providedIn: 'root' })
export class ChatService {
  private connection: signalR.HubConnection | null = null;
  private currentAccessToken: string | null = null;
  private readonly messagesSubject = new BehaviorSubject<ChatRealtimeMessage[]>([]);

  readonly messages$ = this.messagesSubject.asObservable();

  async connect(accessToken: string): Promise<void> {
    if (this.connection?.state === signalR.HubConnectionState.Connected && this.currentAccessToken === accessToken) {
      return;
    }

    await this.disconnect();
    this.currentAccessToken = accessToken;

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl('/ws/chat/hubs/chat', {
        accessTokenFactory: () => accessToken
      })
      .withAutomaticReconnect()
      .build();

    this.connection.on('messageReceived', (message: ChatRealtimeMessage) => {
      this.messagesSubject.next([...this.messagesSubject.value, message]);
    });

    await this.connection.start();
  }

  async disconnect(): Promise<void> {
    if (!this.connection) {
      this.messagesSubject.next([]);
      this.currentAccessToken = null;
      return;
    }

    this.connection.off('messageReceived');

    if (this.connection.state !== signalR.HubConnectionState.Disconnected) {
      await this.connection.stop();
    }

    this.connection = null;
    this.currentAccessToken = null;
    this.messagesSubject.next([]);
  }

  async joinConversation(conversationId: string): Promise<void> {
    await this.connection?.invoke('JoinConversation', conversationId);
  }

  async leaveConversation(conversationId: string): Promise<void> {
    await this.connection?.invoke('LeaveConversation', conversationId);
  }

  async sendMessage(conversationId: string, content: string): Promise<void> {
    await this.connection?.invoke('SendMessage', { conversationId, content });
  }

  replaceMessages(messages: ChatRealtimeMessage[]): void {
    this.messagesSubject.next(messages);
  }
}