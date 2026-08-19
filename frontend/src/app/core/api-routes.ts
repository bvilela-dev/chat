/**
 * Rotas da API, centralizadas num único lugar.
 *
 * Antes desta consolidação, as URLs estavam espalhadas como literais dentro de
 * cada service. Isso é um problema concreto: quando os endpoints do backend
 * mudaram para corrigir as falhas de controle de acesso — `/api/users/{id}/conversations`
 * virou `/api/conversations`, e `/presence/online/{id}` virou `/presence/me/online` —
 * era preciso caçar cada literal pelo projeto. Um esquecimento só apareceria em
 * runtime, como um 404.
 *
 * Com as rotas centralizadas, a mudança fica contida num arquivo, e o
 * TypeScript cobra os parâmetros de cada função.
 */
export const ApiRoutes = {
  identity: {
    register: '/identity/api/auth/register',
    login: '/identity/api/auth/login',
    refresh: '/identity/api/auth/refresh',
    users: '/identity/api/users'
  },

  messages: {
    /**
     * Conversas do usuário autenticado.
     *
     * Note a ausência de parâmetro: o backend deriva o usuário do token JWT.
     * A rota anterior (`/api/users/{userId}/conversations`) permitia ler as
     * conversas de qualquer pessoa apenas trocando o identificador na URL.
     */
    myConversations: '/messages/api/conversations',

    createDirectConversation: '/messages/api/conversations/direct',

    conversationMessages: (conversationId: string, page: number, pageSize: number): string =>
      `/messages/api/conversations/${conversationId}/messages?page=${page}&pageSize=${pageSize}`
  },

  presence: {
    onlineUsers: '/presence/api/presence/online',

    /**
     * Marca o próprio usuário como online. Também funciona como heartbeat:
     * cada chamada renova o TTL do registro no Redis.
     *
     * Sem parâmetro, pelo mesmo motivo das conversas — a rota anterior permitia
     * manipular a presença de terceiros.
     */
    setSelfOnline: '/presence/api/presence/me/online',
    setSelfOffline: '/presence/api/presence/me/offline'
  },

  chat: {
    /** Endpoint do hub SignalR, publicado pelo gateway. */
    hub: '/ws/chat/hubs/chat'
  }
} as const;
