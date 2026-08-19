namespace PresenceService.Domain;

/// <summary>
/// Estado de presença de um usuário.
/// </summary>
/// <param name="UserId">Usuário.</param>
/// <param name="IsOnline">Se há sessão ativa no momento.</param>
/// <param name="LastSeenAtUtc">Último instante conhecido de atividade, em UTC.</param>
/// <remarks>
/// <para>
/// <b>Presença é dado efêmero.</b> Não vai para o PostgreSQL, e sim para o
/// Redis, com expiração automática. O raciocínio: presença perde valor em
/// segundos, é escrita com altíssima frequência (a cada conexão, desconexão e
/// heartbeat) e a perda total desse dado num incidente é irrelevante — todo mundo
/// aparece offline por alguns segundos e o estado se reconstrói sozinho conforme
/// os clientes reportam atividade.
/// </para>
/// <para>
/// Gravar isso num banco relacional seria o antipadrão clássico de usar o
/// armazenamento durável como cache de alta rotatividade.
/// </para>
/// </remarks>
public sealed record UserPresence(Guid UserId, bool IsOnline, DateTime? LastSeenAtUtc);
