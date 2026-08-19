using ChatService.Application.Abstractions;
using StackExchange.Redis;

namespace ChatService.Infrastructure.Realtime;

/// <summary>
/// Mantém em Redis o mapa de conexões WebSocket ativas por usuário.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que Redis e não memória.</b> O Chat Service roda em várias réplicas. A
/// conexão do usuário A pode estar na réplica 1 e a do usuário B na réplica 2.
/// Um dicionário em memória enxergaria apenas as conexões locais, e qualquer
/// decisão baseada nele estaria errada metade do tempo.
/// </para>
/// <para>
/// <b>TTL em todas as chaves.</b> Foi acrescentado nesta revisão. Sem expiração,
/// uma réplica encerrada de forma abrupta (OOM kill, nó do Kubernetes removido)
/// deixaria para trás entradas de conexões que não existem mais — permanentemente,
/// já que ninguém executaria a limpeza. O TTL torna o registro
/// <i>autossaneável</i>: entrada obsoleta desaparece sozinha.
/// </para>
/// </remarks>
public sealed class RedisConnectionRegistry(IConnectionMultiplexer connectionMultiplexer) : IConnectionRegistry
{
    /// <summary>
    /// Validade das chaves de conexão.
    /// </summary>
    /// <remarks>
    /// Precisa ser confortavelmente maior que o intervalo de keep-alive do
    /// SignalR (15 s por padrão). Duas horas cobrem sessões longas com folga; a
    /// renovação ocorre a cada reconexão.
    /// </remarks>
    private static readonly TimeSpan ConnectionTtl = TimeSpan.FromHours(2);

    /// <inheritdoc />
    public async Task RegisterConnectionAsync(Guid userId, string connectionId, CancellationToken cancellationToken)
    {
        var database = connectionMultiplexer.GetDatabase();

        var userConnectionsKey = BuildUserConnectionsKey(userId);
        var connectionOwnerKey = BuildConnectionOwnerKey(connectionId);

        // Um SET por usuário: um mesmo usuário pode estar conectado por vários
        // dispositivos simultaneamente.
        await database.SetAddAsync(userConnectionsKey, connectionId);
        await database.KeyExpireAsync(userConnectionsKey, ConnectionTtl);

        // Índice inverso: dado o id da conexão, quem é o dono. Usado no
        // encerramento, quando o contexto do usuário já pode não estar disponível.
        await database.StringSetAsync(connectionOwnerKey, userId.ToString(), ConnectionTtl);
    }

    /// <inheritdoc />
    public async Task RemoveConnectionAsync(Guid userId, string connectionId, CancellationToken cancellationToken)
    {
        var database = connectionMultiplexer.GetDatabase();

        await database.SetRemoveAsync(BuildUserConnectionsKey(userId), connectionId);
        await database.KeyDeleteAsync(BuildConnectionOwnerKey(connectionId));
    }

    private static string BuildUserConnectionsKey(Guid userId) => $"user:{userId}:connections";

    private static string BuildConnectionOwnerKey(string connectionId) => $"connection:{connectionId}:user";
}
