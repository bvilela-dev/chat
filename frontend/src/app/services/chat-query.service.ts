import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map, Observable } from 'rxjs';
import { ApiRoutes } from '../core/api-routes';
import { ChatRealtimeMessage, ConversationReadDto, MessageReadDto } from '../models/chat.models';

/**
 * Consultas ao lado de leitura do Message Service (histórico e conversas).
 *
 * É o caminho "lento e durável" do chat. O caminho rápido é o SignalR, em
 * `ChatService`: mensagens novas chegam por lá em tempo real, e este serviço é
 * consultado apenas ao abrir uma conversa, para carregar o que já existia.
 */
@Injectable({ providedIn: 'root' })
export class ChatQueryService {
  private readonly http = inject(HttpClient);

  /** Tamanho da página de histórico carregada ao abrir uma conversa. */
  private static readonly HistoryPageSize = 100;

  /**
   * Lista as conversas do usuário autenticado.
   *
   * A rota não recebe identificador de usuário — o backend o deriva do token
   * JWT. A versão anterior chamava `/api/users/{userId}/conversations`, que
   * permitia ler as conversas de qualquer pessoa trocando o GUID na URL.
   */
  getMyConversations(): Observable<ConversationReadDto[]> {
    return this.http.get<ConversationReadDto[]>(ApiRoutes.messages.myConversations);
  }

  /**
   * Carrega o histórico de uma conversa.
   *
   * O backend responde 403 se o usuário autenticado não participar da conversa —
   * verificação que não existia antes.
   */
  getMessages(conversationId: string): Observable<ChatRealtimeMessage[]> {
    return this.http
      .get<MessageReadDto[]>(
        ApiRoutes.messages.conversationMessages(conversationId, 1, ChatQueryService.HistoryPageSize)
      )
      .pipe(
        // Traduz o formato do read model para o formato de tempo real, que é o
        // que a interface consome. Assim a lista de mensagens tem uma única
        // forma, independentemente de a mensagem ter vindo do histórico ou do
        // WebSocket.
        map((items) => items.map(toRealtimeMessage))
      );
  }

  /** Abre (ou reaproveita) a conversa direta com outro usuário. */
  createDirectConversation(participantId: string): Observable<ConversationReadDto> {
    return this.http.post<ConversationReadDto>(
      ApiRoutes.messages.createDirectConversation,
      { participantId }
    );
  }
}

function toRealtimeMessage(message: MessageReadDto): ChatRealtimeMessage {
  return {
    messageId: message.id,
    conversationId: message.conversationId,
    senderId: message.senderId,
    senderName: message.senderName,
    content: message.content,
    createdAtUtc: message.createdAtUtc
  };
}
