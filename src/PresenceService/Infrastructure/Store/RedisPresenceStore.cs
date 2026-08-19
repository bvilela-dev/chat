using System.Globalization;
using PresenceService.Application.Abstractions;
using PresenceService.Domain;
using StackExchange.Redis;

namespace PresenceService.Infrastructure.Store;

/// <summary>
/// Armazena o estado de presença em Redis.
/// </summary>
/// <remarks>
/// <para>
/// Esta classe concentra duas correções relevantes em relação à versão original.
/// </para>
///
/// <para>
/// <b>1. TTL nas chaves de "online".</b> Antes, <c>SetOnline</c> gravava a chave
/// sem expiração. Como consequência, qualquer encerramento que não passasse pelo
/// caminho feliz — aba fechada à força, queda de rede, processo morto por OOM —
/// deixava o usuário marcado como online <i>para sempre</i>. Na prática, a lista
/// de usuários online só crescia, e nunca refletia a realidade.
/// </para>
/// <para>
/// Com TTL, a presença passa a ser uma afirmação com prazo: "estou aqui, e
/// reafirmo isso periodicamente". Silêncio prolongado significa ausência. É o
/// mesmo modelo usado por sistemas de service discovery e por eleição de líder.
/// </para>
///
/// <para>
/// <b>2. Fim do <c>KEYS</c> / <c>SCAN</c> para listar quem está online.</b> A
/// implementação anterior fazia:
/// </para>
/// <code>
/// server.Keys(pattern: "user:*:online")   // varre TODO o keyspace
/// </code>
/// <para>
/// O custo é O(N) sobre o número total de chaves do Redis — não sobre o número
/// de usuários online. Como este mesmo Redis também guarda o backplane do
/// SignalR, o registro de conexões e a inbox do Notification Service, a varredura
/// percorre estruturas que nada têm a ver com presença. E o Redis é
/// single-threaded: uma varredura longa <b>bloqueia todos os outros comandos</b>,
/// travando o chat inteiro.
/// </para>
/// <para>
/// A solução é manter um SET com os usuários online. Listar vira <c>SMEMBERS</c>,
/// que é O(N) apenas sobre os online. O preço é reconciliar o SET com as chaves
/// que expiraram sozinhas — feito de forma preguiçosa na leitura (ver
/// <see cref="GetOnlineAsync"/>).
/// </para>
/// </remarks>
public sealed class RedisPresenceStore(IConnectionMultiplexer connectionMultiplexer) : IPresenceStore
{
    /// <summary>
    /// Validade de uma marcação de presença sem renovação.
    /// </summary>
    /// <remarks>
    /// Precisa ser maior que o intervalo de heartbeat do cliente (10 s no
    /// frontend) com folga suficiente para absorver um pico de latência ou a
    /// aba do navegador ser suspensa em segundo plano. 60 segundos dá margem de
    /// seis batidas perdidas antes de considerar o usuário ausente.
    /// </remarks>
    private static readonly TimeSpan OnlineTtl = TimeSpan.FromSeconds(60);

    /// <summary>Retenção do registro de "visto por último".</summary>
    private static readonly TimeSpan LastSeenTtl = TimeSpan.FromDays(30);

    /// <summary>Chave do conjunto de usuários online.</summary>
    private const string OnlineUsersSetKey = "presence:online-users";

    /// <inheritdoc />
    public async Task<UserPresence> SetOnlineAsync(Guid userId, DateTime occurredAtUtc, CancellationToken cancellationToken)
    {
        var database = connectionMultiplexer.GetDatabase();

        // Duas escritas coordenadas:
        //   1. a chave individual, com TTL — é ela que expira sozinha;
        //   2. a entrada no SET, que torna a listagem barata.
        //
        // O SET não tem TTL por membro (o Redis não oferece isso), e é por essa
        // razão que a leitura precisa reconciliar as duas estruturas.
        await database.StringSetAsync(BuildOnlineKey(userId), "1", OnlineTtl);
        await database.SetAddAsync(OnlineUsersSetKey, userId.ToString());

        // Atualiza o "visto por última vez" já na entrada. Assim, se o usuário
        // desaparecer sem chamar SetOffline, o último instante conhecido continua
        // razoavelmente preciso.
        await database.StringSetAsync(BuildLastSeenKey(userId), FormatInstant(occurredAtUtc), LastSeenTtl);

        return new UserPresence(userId, IsOnline: true, occurredAtUtc);
    }

    /// <inheritdoc />
    public async Task<UserPresence> SetOfflineAsync(Guid userId, DateTime occurredAtUtc, CancellationToken cancellationToken)
    {
        var database = connectionMultiplexer.GetDatabase();

        await database.KeyDeleteAsync(BuildOnlineKey(userId));
        await database.SetRemoveAsync(OnlineUsersSetKey, userId.ToString());
        await database.StringSetAsync(BuildLastSeenKey(userId), FormatInstant(occurredAtUtc), LastSeenTtl);

        return new UserPresence(userId, IsOnline: false, occurredAtUtc);
    }

    /// <inheritdoc />
    public async Task<UserPresence> GetStatusAsync(Guid userId, CancellationToken cancellationToken)
    {
        var database = connectionMultiplexer.GetDatabase();

        var isOnline = await database.KeyExistsAsync(BuildOnlineKey(userId));
        var lastSeenAtUtc = await ReadLastSeenAsync(database, userId);

        return new UserPresence(userId, isOnline, lastSeenAtUtc);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<UserPresence>> GetOnlineAsync(CancellationToken cancellationToken)
    {
        var database = connectionMultiplexer.GetDatabase();

        var candidates = await database.SetMembersAsync(OnlineUsersSetKey);
        if (candidates.Length == 0)
        {
            return [];
        }

        // RECONCILIAÇÃO PREGUIÇOSA.
        //
        // O SET pode conter usuários cuja chave individual já expirou (saíram sem
        // avisar). Confirmamos cada candidato contra a chave com TTL e removemos
        // do SET os que não existem mais.
        //
        // As verificações vão num único pipeline: N comandos numa só ida à rede,
        // em vez de N idas. É a diferença entre ~1 ms e ~50 ms para 50 usuários.
        var onlineKeyChecks = candidates
            .Select(member => new
            {
                Member = member,
                UserId = Guid.TryParse(member.ToString(), out var parsed) ? parsed : (Guid?)null
            })
            .Where(candidate => candidate.UserId is not null)
            .Select(candidate => new
            {
                candidate.Member,
                UserId = candidate.UserId!.Value,
                ExistsTask = database.KeyExistsAsync(BuildOnlineKey(candidate.UserId!.Value))
            })
            .ToArray();

        await Task.WhenAll(onlineKeyChecks.Select(check => check.ExistsTask));

        var staleMembers = onlineKeyChecks
            .Where(check => !check.ExistsTask.Result)
            .Select(check => check.Member)
            .ToArray();

        if (staleMembers.Length > 0)
        {
            // Limpeza oportunista: sem ela, o SET acumularia indefinidamente os
            // usuários que já saíram, e a reconciliação ficaria mais cara a cada
            // leitura.
            await database.SetRemoveAsync(OnlineUsersSetKey, [.. staleMembers.Select(member => (RedisValue)member)]);
        }

        var onlineUserIds = onlineKeyChecks
            .Where(check => check.ExistsTask.Result)
            .Select(check => check.UserId)
            .ToArray();

        if (onlineUserIds.Length == 0)
        {
            return [];
        }

        var lastSeenTasks = onlineUserIds.ToDictionary(
            userId => userId,
            userId => ReadLastSeenAsync(database, userId));

        await Task.WhenAll(lastSeenTasks.Values);

        return
        [
            .. onlineUserIds.Select(userId =>
                new UserPresence(userId, IsOnline: true, lastSeenTasks[userId].Result))
        ];
    }

    private static async Task<DateTime?> ReadLastSeenAsync(IDatabase database, Guid userId)
    {
        var value = await database.StringGetAsync(BuildLastSeenKey(userId));

        if (value.IsNullOrEmpty)
        {
            return null;
        }

        // Parsing estrito, com cultura invariante e estilo UTC explícito.
        //
        // O código anterior usava `DateTime.TryParse(value, out var result)`, que
        // aplica a cultura do sistema operacional e devolve um DateTime com Kind
        // "Unspecified" — o instante era gravado em UTC e lido como se fosse
        // local. Num servidor em UTC-3, todo "visto por último" aparecia com 3
        // horas de diferença.
        return DateTime.TryParse(
            value.ToString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind | DateTimeStyles.AdjustToUniversal,
            out var lastSeenAtUtc)
            ? lastSeenAtUtc
            : null;
    }

    /// <summary>
    /// Formata um instante no padrão ISO 8601 com informação de fuso ("O").
    /// </summary>
    /// <remarks>
    /// O formato "round-trip" preserva a precisão e o <c>Kind</c> do
    /// <see cref="DateTime"/>, garantindo que o valor lido seja idêntico ao
    /// gravado.
    /// </remarks>
    private static string FormatInstant(DateTime instant)
    {
        return instant.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    private static string BuildOnlineKey(Guid userId) => $"user:{userId}:online";

    private static string BuildLastSeenKey(Guid userId) => $"user:{userId}:last_seen";
}
