import { Injectable } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { BehaviorSubject } from 'rxjs';
import { ChatRealtimeMessage } from '../models/chat.models';

@Injectable({ providedIn: 'root' })
export class ChatService {
  private connection: signalR.HubConnection | null = null;
  private readonly messagesSubject = new BehaviorSubject<ChatRealtimeMessage[]>([]);

  readonly messages$ = this.messagesSubject.asObservable();

  async connect(accessToken: string): Promise<void> {
    if (this.connection?.state === signalR.HubConnectionState.Connected) {
      return;
    }

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl('/chat/hubs/chat', {
        accessTokenFactory: () => accessToken
      })
      .withAutomaticReconnect()
      .build();

    this.connection.on('messageReceived', (message: ChatRealtimeMessage) => {
      this.messagesSubject.next([...this.messagesSubject.value, message]);
    });

    await this.connection.start();
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