using NotificationService.Application.Abstractions;
using StackExchange.Redis;

namespace NotificationService.Infrastructure.Store;

/// <summary>
/// Projeção local de participantes por conversa, mantida em Redis.
/// </summary>
/// <remarks>
/// Construída a partir dos eventos de entrada e saída publicados pelo Chat
/// Service. Ver <see cref="IConversationMembershipStore"/> para o raciocínio de
/// por que este serviço mantém a própria cópia.
/// </remarks>
public sealed class RedisConversationMembershipStore(IConnectionMultiplexer connectionMultiplexer)
    : IConversationMembershipStore
{
    /// <summary>
    /// Retenção dos registros de deduplicação.
    /// </summary>
    /// <remarks>
    /// Precisa ser confortavelmente maior que a janela máxima em que o broker
    /// poderia reentregar uma mensagem (retries + tempo parado numa fila). Sete
    /// dias cobrem com folga, e o TTL evita que a chave de idempotência cresça
    /// indefinidamente — este é o principal ganho de usar Redis, e não uma tabela,
    /// para a inbox deste serviço.
    /// </remarks>
    private static readonly TimeSpan InboxRetention = TimeSpan.FromDays(7);

    /// <inheritdoc />
    public Task AddParticipantAsync(Guid conversationId, Guid userId, CancellationToken cancellationToken)
    {
        return connectionMultiplexer.GetDatabase()
            .SetAddAsync(BuildParticipantsKey(conversationId), userId.ToString());
    }

    /// <inheritdoc />
    public Task RemoveParticipantAsync(Guid conversationId, Guid userId, CancellationToken cancellationToken)
    {
        return connectionMultiplexer.GetDatabase()
            .SetRemoveAsync(BuildParticipantsKey(conversationId), userId.ToString());
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<Guid>> GetParticipantsAsync(
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var members = await connectionMultiplexer.GetDatabase()
            .SetMembersAsync(BuildParticipantsKey(conversationId));

        // Valores malformados são descartados em silêncio em vez de derrubar o
        // consumo. Uma entrada corrompida não deve impedir a notificação dos
        // demais participantes.
        return
        [
            .. members
                .Select(member => Guid.TryParse(member.ToString(), out var userId) ? userId : (Guid?)null)
                .Where(userId => userId is not null)
                .Select(userId => userId!.Value)
        ];
    }

    /// <inheritdoc />
    public Task<bool> HasProcessedAsync(Guid eventId, string consumerName, CancellationToken cancellationToken)
    {
        return connectionMultiplexer.GetDatabase().KeyExistsAsync(BuildInboxKey(eventId, consumerName));
    }

    /// <inheritdoc />
    public Task MarkProcessedAsync(Guid eventId, string consumerName, CancellationToken cancellationToken)
    {
        return connectionMultiplexer.GetDatabase()
            .StringSetAsync(BuildInboxKey(eventId, consumerName), "1", InboxRetention);
    }

    private static string BuildParticipantsKey(Guid conversationId) =>
        $"conversation:{conversationId}:participants";

    private static string BuildInboxKey(Guid eventId, string consumerName) =>
        $"notification:inbox:{consumerName}:{eventId}";
}

/// <summary>
/// Consulta a presença lendo as chaves gravadas pelo Presence Service.
/// </summary>
/// <remarks>
/// O formato da chave é um contrato implícito entre os dois serviços — a
/// fragilidade documentada em <see cref="IPresenceLookup"/>. A constante abaixo é
/// o ponto único a alterar caso o Presence Service mude o esquema de chaves.
/// </remarks>
public sealed class RedisPresenceLookup(IConnectionMultiplexer connectionMultiplexer) : IPresenceLookup
{
    /// <inheritdoc />
    public Task<bool> IsOnlineAsync(Guid userId, CancellationToken cancellationToken)
    {
        // A chave tem TTL no Presence Service, então "existe" já significa
        // "online e reafirmado recentemente" — não é preciso checar validade aqui.
        return connectionMultiplexer.GetDatabase().KeyExistsAsync($"user:{userId}:online");
    }
}
