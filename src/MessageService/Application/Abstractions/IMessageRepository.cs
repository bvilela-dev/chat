using BuildingBlocks.Contracts;
using MessageService.Application.Contracts;
using MessageService.Domain;

namespace MessageService.Application.Abstractions;

/// <summary>
/// Porta de acesso ao armazenamento de conversas e mensagens.
/// </summary>
/// <remarks>
/// Cobre os dois lados do CQRS. A separação física entre bancos de escrita e de
/// leitura seria o próximo passo natural em escala; aqui os dois modelos
/// convivem no mesmo PostgreSQL, mas em tabelas distintas — o que já permite
/// otimizar índices de forma independente e viabiliza a separação futura sem
/// alterar a camada de aplicação.
/// </remarks>
public interface IMessageRepository
{
    // ---------------------------------------------------------------------
    // Idempotência (inbox)
    // ---------------------------------------------------------------------

    /// <summary>Indica se este consumidor já processou o evento informado.</summary>
    Task<bool> HasProcessedAsync(Guid eventId, string consumerName, CancellationToken cancellationToken);

    /// <summary>Registra o processamento de um evento por um consumidor.</summary>
    Task MarkProcessedAsync(Guid eventId, string consumerName, DateTime processedAtUtc, CancellationToken cancellationToken);

    // ---------------------------------------------------------------------
    // Autorização
    // ---------------------------------------------------------------------

    /// <summary>
    /// Indica se o usuário participa da conversa.
    /// </summary>
    /// <remarks>
    /// <b>É a consulta de autorização central do serviço.</b> Toda leitura de
    /// histórico e toda entrada em sala de tempo real passam por aqui. Antes das
    /// correções, ela não era chamada em lugar nenhum.
    /// </remarks>
    Task<bool> IsParticipantAsync(Guid conversationId, Guid userId, CancellationToken cancellationToken);

    // ---------------------------------------------------------------------
    // Escrita
    // ---------------------------------------------------------------------

    /// <summary>Indica se a mensagem já foi persistida (deduplicação por id).</summary>
    Task<bool> MessageExistsAsync(Guid messageId, CancellationToken cancellationToken);

    /// <summary>Busca uma conversa pelo identificador.</summary>
    Task<Conversation?> GetConversationAsync(Guid conversationId, CancellationToken cancellationToken);

    /// <summary>Localiza a conversa direta existente entre dois usuários.</summary>
    Task<ConversationSummary?> GetDirectConversationAsync(Guid firstUserId, Guid secondUserId, CancellationToken cancellationToken);

    /// <summary>Marca uma conversa para inclusão.</summary>
    Task AddConversationAsync(Conversation conversation, CancellationToken cancellationToken);

    /// <summary>Cria uma conversa direta com seus dois participantes e projeções.</summary>
    Task CreateDirectConversationAsync(Guid conversationId, Guid initiatorId, Guid participantId, DateTime createdAtUtc, CancellationToken cancellationToken);

    /// <summary>Adiciona um participante, se ainda não houver.</summary>
    Task AddParticipantAsync(Guid conversationId, Guid userId, DateTime joinedAtUtc, CancellationToken cancellationToken);

    /// <summary>Remove um participante da conversa.</summary>
    Task RemoveParticipantAsync(Guid conversationId, Guid userId, CancellationToken cancellationToken);

    /// <summary>Marca uma mensagem para inclusão.</summary>
    void AddMessage(Message message);

    /// <summary>Enfileira um evento de integração na outbox da transação corrente.</summary>
    void EnqueueOutbox(IIntegrationEvent integrationEvent);

    // ---------------------------------------------------------------------
    // Projeções e leitura
    // ---------------------------------------------------------------------

    /// <summary>Cria ou atualiza as projeções afetadas por uma nova mensagem.</summary>
    Task UpsertProjectionAsync(MessageProjectionRequested projection, CancellationToken cancellationToken);

    /// <summary>Lê uma página do histórico de mensagens de uma conversa.</summary>
    Task<IReadOnlyCollection<MessageReadModel>> GetMessagesByConversationAsync(Guid conversationId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>Lista as conversas em que o usuário participa.</summary>
    Task<IReadOnlyCollection<ConversationSummary>> GetUserConversationsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Confirma as alterações pendentes numa única transação.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
