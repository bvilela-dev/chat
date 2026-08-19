using BuildingBlocks.Contracts.Grpc;
using ChatService.Application.Abstractions;
using Grpc.Core;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace ChatService.Infrastructure.Access;

/// <summary>
/// Implementa a política de acesso consultando o Message Service via gRPC, com
/// cache local de curta duração.
/// </summary>
/// <remarks>
/// <para>
/// <b>O compromisso central desta classe.</b> A verificação precisa ser feita a
/// cada entrada em conversa e a cada mensagem enviada. Sem cache, um usuário
/// ativo geraria dezenas de chamadas de rede por minuto, e o Chat Service
/// passaria a depender do Message Service com uma frequência que o tornaria um
/// ponto único de falha efetivo.
/// </para>
/// <para>
/// Com cache, a janela de exposição vira a duração da entrada: um usuário
/// removido de uma conversa pode continuar acessando por até
/// <see cref="CacheDuration"/>. Trinta segundos é o meio-termo escolhido —
/// tempo suficiente para absorver a rajada de mensagens de uma conversa ativa,
/// curto o bastante para que a remoção tenha efeito prático imediato do ponto de
/// vista humano.
/// </para>
/// <para>
/// <b>Só o resultado positivo é cacheado.</b> A negativa é reconsultada sempre.
/// A assimetria é proposital: cachear "não pode" impediria alguém que acabou de
/// ser adicionado à conversa de entrar, gerando um bug visível e confuso;
/// cachear "pode" apenas estende levemente um acesso já concedido.
/// </para>
/// </remarks>
public sealed class GrpcConversationAccessPolicy(
    ConversationAccessGrpc.ConversationAccessGrpcClient client,
    IMemoryCache cache,
    ILogger<GrpcConversationAccessPolicy> logger)
    : IConversationAccessPolicy
{
    /// <summary>Duração da entrada de cache para uma autorização concedida.</summary>
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Prazo máximo para a consulta de autorização.
    /// </summary>
    /// <remarks>
    /// Um <i>deadline</i> explícito é obrigatório em chamada gRPC síncrona no
    /// caminho crítico. Sem ele, uma instância lenta do Message Service faria as
    /// requisições se acumularem aqui até esgotar o pool de threads — e o Chat
    /// Service cairia junto, propagando a falha. Com deadline, a chamada falha
    /// rápido e o erro fica contido.
    /// </remarks>
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(3);

    /// <inheritdoc />
    public async Task<bool> CanAccessConversationAsync(
        Guid conversationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var cacheKey = BuildCacheKey(conversationId, userId);

        if (cache.TryGetValue<bool>(cacheKey, out var cachedResult) && cachedResult)
        {
            return true;
        }

        var isParticipant = await QueryMessageServiceAsync(conversationId, userId, cancellationToken);

        if (isParticipant)
        {
            cache.Set(cacheKey, true, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheDuration,

                // Limita o crescimento do cache: sem um teto de tamanho, o
                // dicionário cresceria com cada par (conversa, usuário) já visto
                // e viraria um vazamento de memória lento em processos de longa
                // duração.
                Size = 1
            });
        }

        return isParticipant;
    }

    private async Task<bool> QueryMessageServiceAsync(
        Guid conversationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await client.CheckMembershipAsync(
                new CheckMembershipRequest
                {
                    ConversationId = conversationId.ToString(),
                    UserId = userId.ToString()
                },
                deadline: DateTime.UtcNow.Add(CallTimeout),
                cancellationToken: cancellationToken);

            return response.IsParticipant;
        }
        catch (RpcException exception)
        {
            // ===== FALHA FECHADA (fail closed) =====
            //
            // Message Service indisponível significa que NÃO É POSSÍVEL confirmar
            // a autorização. A resposta correta é negar.
            //
            // A alternativa — liberar em caso de erro, "para não atrapalhar o
            // usuário" — transformaria uma indisponibilidade em brecha de
            // segurança: bastaria derrubar o Message Service para ler qualquer
            // conversa. Componentes de autorização devem sempre falhar fechados.
            //
            // O efeito colateral aceito é que o chat em tempo real degrada quando
            // o Message Service cai. É a escolha certa: preferimos indisponível a
            // inseguro.
            logger.LogError(
                exception,
                "Não foi possível verificar a participação do usuário {UserId} na conversa {ConversationId}. " +
                "Acesso negado por precaução.",
                userId,
                conversationId);

            return false;
        }
    }

    /// <summary>
    /// Monta a chave de cache.
    /// </summary>
    /// <remarks>
    /// Prefixo explícito porque o <see cref="IMemoryCache"/> é compartilhado por
    /// todo o processo: sem ele, haveria risco de colisão com entradas de outro
    /// componente que usasse a mesma combinação de GUIDs.
    /// </remarks>
    private static string BuildCacheKey(Guid conversationId, Guid userId)
    {
        return $"conversation-access:{conversationId}:{userId}";
    }
}
