import { Injectable, signal } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { ApiRoutes } from '../core/api-routes';
import { ChatRealtimeMessage } from '../models/chat.models';

/**
 * Conexão SignalR e mensagens em tempo real.
 *
 * É o "caminho rápido" do chat: mensagens chegam por aqui em milissegundos. O
 * histórico durável vem do `ChatQueryService`, via HTTP.
 */
@Injectable({ providedIn: 'root' })
export class ChatService {
  private connection: signalR.HubConnection | null = null;
  private currentAccessToken: string | null = null;

  private readonly messages = signal<ChatRealtimeMessage[]>([]);

  /** Mensagens da conversa aberta, em ordem cronológica. */
  readonly messages$ = this.messages.asReadonly();

  /** Estado atual da conexão, para a interface poder sinalizar reconexão. */
  private readonly connectionState = signal<signalR.HubConnectionState>(
    signalR.HubConnectionState.Disconnected
  );

  /** Estado da conexão em tempo real. */
  readonly connectionState$ = this.connectionState.asReadonly();

  /**
   * Abre a conexão com o hub.
   *
   * @param accessToken JWT usado para autenticar o handshake.
   */
  async connect(accessToken: string): Promise<void> {
    const alreadyConnected =
      this.connection?.state === signalR.HubConnectionState.Connected &&
      this.currentAccessToken === accessToken;

    if (alreadyConnected) {
      return;
    }

    await this.disconnect();
    this.currentAccessToken = accessToken;

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(ApiRoutes.chat.hub, {
        // A API de WebSocket do navegador não permite cabeçalhos personalizados
        // no handshake, então o token vai pela query string. O backend só aceita
        // essa forma no caminho do hub — ver JwtAuthenticationExtensions.
        //
        // A função é chamada A CADA (re)conexão, e não uma única vez. Isso
        // importa: numa reconexão após uma renovação de sessão, ela precisa
        // devolver o token ATUAL. Capturar o valor uma vez faria a reconexão
        // falhar com o token velho.
        accessTokenFactory: () => this.currentAccessToken ?? accessToken
      })
      // Backoff explícito em vez do padrão da biblioteca (que desiste após ~60s).
      // Perder a rede por dois minutos é rotina em conexão móvel; desistir da
      // reconexão obrigaria o usuário a recarregar a página.
      .withAutomaticReconnect([0, 2_000, 5_000, 10_000, 30_000])
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    this.registerHandlers();

    await this.connection.start();
    this.connectionState.set(this.connection.state);
  }

  /** Encerra a conexão e limpa o estado local. */
  async disconnect(): Promise<void> {
    const connection = this.connection;

    // Zera o estado ANTES de aguardar o encerramento, para que a interface não
    // continue exibindo mensagens de uma sessão que já acabou.
    this.connection = null;
    this.currentAccessToken = null;
    this.messages.set([]);
    this.connectionState.set(signalR.HubConnectionState.Disconnected);

    if (!connection) {
      return;
    }

    connection.off('messageReceived');

    if (connection.state !== signalR.HubConnectionState.Disconnected) {
      await connection.stop();
    }
  }

  /**
   * Entra na sala de uma conversa.
   *
   * @throws Quando o usuário não participa da conversa: o backend devolve uma
   * `HubException` com a mensagem "Você não participa desta conversa". Essa
   * verificação não existia antes — qualquer usuário podia entrar em qualquer
   * conversa informando o identificador.
   */
  async joinConversation(conversationId: string): Promise<void> {
    await this.connection?.invoke('JoinConversation', conversationId);
  }

  /** Sai da sala de uma conversa. */
  async leaveConversation(conversationId: string): Promise<void> {
    await this.connection?.invoke('LeaveConversation', conversationId);
  }

  /** Envia uma mensagem para a conversa aberta. */
  async sendMessage(conversationId: string, content: string): Promise<void> {
    await this.connection?.invoke('SendMessage', { conversationId, content });
  }

  /** Substitui a lista de mensagens (usado ao carregar o histórico). */
  replaceMessages(messages: ChatRealtimeMessage[]): void {
    this.messages.set(messages);
  }

  private registerHandlers(): void {
    if (!this.connection) {
      return;
    }

    this.connection.on('messageReceived', (message: ChatRealtimeMessage) => {
      // DEDUPLICAÇÃO NO CLIENTE.
      //
      // A mesma mensagem pode chegar duas vezes: uma pelo tempo real e outra
      // pelo histórico, se o usuário abrir a conversa no exato instante em que
      // ela é enviada. O `messageId` é gerado pelo servidor e é o mesmo nos dois
      // caminhos, o que torna a verificação confiável.
      this.messages.update((current) =>
        current.some((existing) => existing.messageId === message.messageId)
          ? current
          : [...current, message]
      );
    });

    // Espelha o estado da conexão para a interface poder exibir "reconectando".
    // Sem esse feedback, o usuário digita mensagens que não são enviadas e não
    // entende o motivo.
    this.connection.onreconnecting(() =>
      this.connectionState.set(signalR.HubConnectionState.Reconnecting)
    );

    this.connection.onreconnected(() =>
      this.connectionState.set(signalR.HubConnectionState.Connected)
    );

    this.connection.onclose(() =>
      this.connectionState.set(signalR.HubConnectionState.Disconnected)
    );
  }
}
