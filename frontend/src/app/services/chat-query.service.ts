import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map, Observable } from 'rxjs';
import { ChatRealtimeMessage, ConversationReadDto, MessageReadDto } from '../models/chat.models';

@Injectable({ providedIn: 'root' })
export class ChatQueryService {
  private readonly http = inject(HttpClient);

  getUserConversations(userId: string): Observable<ConversationReadDto[]> {
    return this.http.get<ConversationReadDto[]>(`/messages/api/users/${userId}/conversations`);
  }

  getMessages(conversationId: string): Observable<ChatRealtimeMessage[]> {
    return this.http
      .get<MessageReadDto[]>(`/messages/api/conversations/${conversationId}/messages?page=1&pageSize=100`)
      .pipe(
        map((items) => items.map((item) => ({
          messageId: item.id,
          conversationId: item.conversationId,
          senderId: item.senderId,
          senderName: item.senderName,
          content: item.content,
          createdAtUtc: item.createdAtUtc
        })))
      );
  }

  createDirectConversation(participantId: string): Observable<ConversationReadDto> {
    return this.http.post<ConversationReadDto>('/messages/api/conversations/direct', { participantId });
  }
}