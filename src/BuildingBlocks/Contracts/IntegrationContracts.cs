namespace BuildingBlocks.Contracts;

// =============================================================================
// CONTRATOS DE INTEGRAÇÃO
//
// Este é o único assembly compartilhado por TODOS os microsserviços, e o único
// lugar onde acoplamento entre eles é aceitável — porque é acoplamento a um
// CONTRATO, não a uma implementação.
//
// REGRA DE OURO: eventos de integração são uma API pública. Uma vez publicados,
// só podem evoluir de forma retrocompatível. Concretamente:
//
//   PODE   → adicionar uma propriedade opcional (consumidores antigos a ignoram)
//   NÃO PODE → remover ou renomear uma propriedade
//   NÃO PODE → mudar o tipo de uma propriedade
//   NÃO PODE → alterar o significado de uma propriedade existente
//
// O motivo é operacional: durante um deploy gradual, versões diferentes do
// produtor e do consumidor convivem por minutos ou horas. E mensagens antigas
// podem estar paradas numa fila há dias. Uma mudança incompatível não quebra o
// build — ela quebra em produção, em runtime, de forma intermitente.
//
// Quando a mudança for realmente incompatível, o caminho é publicar um novo
// evento (`MessageSentEventV2`) e manter os dois em paralelo até que todos os
// consumidores migrem.
// =============================================================================

/// <summary>
/// Contrato mínimo de um evento de integração.
/// </summary>
/// <remarks>
/// Os dois membros não são burocracia: <see cref="EventId"/> é a chave de
/// deduplicação usada pela inbox dos consumidores, e <see cref="OccurredAtUtc"/>
/// permite ordenar eventos e medir a latência ponta a ponta do pipeline.
/// </remarks>
public interface IIntegrationEvent
{
    /// <summary>
    /// Identificador único desta ocorrência do evento.
    /// </summary>
    /// <remarks>
    /// Gerado uma única vez pelo produtor e <b>preservado em toda republicação</b>.
    /// Se o despachante da outbox gerasse um id novo a cada tentativa, a
    /// deduplicação do consumidor deixaria de funcionar — cada retentativa
    /// pareceria um evento inédito.
    /// </remarks>
    Guid EventId { get; }

    /// <summary>Instante em que o fato ocorreu, em UTC.</summary>
    /// <remarks>
    /// É o momento do FATO no domínio, não o da publicação. A distinção importa
    /// porque um evento pode ficar minutos na outbox antes de sair: usar o
    /// instante da publicação distorceria a ordenação cronológica das mensagens.
    /// </remarks>
    DateTime OccurredAtUtc { get; }
}

/// <summary>Base comum dos eventos de integração.</summary>
public abstract record IntegrationEvent(Guid EventId, DateTime OccurredAtUtc) : IIntegrationEvent;

/// <summary>
/// Um usuário foi cadastrado.
/// </summary>
/// <remarks>
/// Publicado pelo Identity Service via outbox. É o mecanismo pelo qual os demais
/// serviços tomam conhecimento de um novo usuário <b>sem consultar o banco de
/// identidade</b> — que é privado do Identity Service.
/// </remarks>
public sealed record UserCreatedEvent(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid UserId,
    string Name,
    string Email) : IntegrationEvent(EventId, OccurredAtUtc);

/// <summary>
/// Uma mensagem foi enviada numa conversa.
/// </summary>
/// <remarks>
/// <para>
/// O evento central da plataforma. Publicado pelo Chat Service e consumido por
/// dois serviços com propósitos distintos:
/// </para>
/// <list type="bullet">
///   <item><description>
///   <b>Message Service</b> — persiste a mensagem e dispara a projeção do
///   histórico.
///   </description></item>
///   <item><description>
///   <b>Notification Service</b> — avisa os participantes que estão offline.
///   </description></item>
/// </list>
/// <para>
/// É a vantagem prática do modelo publish/subscribe: acrescentar um terceiro
/// consumidor (indexação para busca, moderação de conteúdo, analytics) não
/// requer nenhuma alteração no Chat Service.
/// </para>
/// <para>
/// O <c>MessageId</c> é gerado pelo produtor, e não pelo banco. Isso é o que
/// permite ao consumidor detectar reprocessamento comparando com o dado já
/// persistido, além de deixar a mensagem identificável em tempo real antes mesmo
/// de existir no banco.
/// </para>
/// </remarks>
public sealed record MessageSentEvent(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid MessageId,
    Guid ConversationId,
    Guid SenderId,
    string SenderName,
    string Content) : IntegrationEvent(EventId, OccurredAtUtc);

/// <summary>Um usuário entrou numa conversa.</summary>
public sealed record ConversationJoinedEvent(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid ConversationId,
    Guid UserId) : IntegrationEvent(EventId, OccurredAtUtc);

/// <summary>Um usuário saiu de uma conversa.</summary>
public sealed record ConversationLeftEvent(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid ConversationId,
    Guid UserId) : IntegrationEvent(EventId, OccurredAtUtc);

/// <summary>Um usuário ficou online.</summary>
public sealed record UserOnlineEvent(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid UserId) : IntegrationEvent(EventId, OccurredAtUtc);

/// <summary>Um usuário ficou offline.</summary>
public sealed record UserOfflineEvent(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid UserId,
    DateTime LastSeenAtUtc) : IntegrationEvent(EventId, OccurredAtUtc);

/// <summary>
/// Nomes de filas e canais compartilhados pela plataforma.
/// </summary>
/// <remarks>
/// <para>
/// Nomes de fila são <b>infraestrutura durável</b>: existem no broker, com
/// mensagens dentro, independentemente do código que os criou. Por isso são
/// constantes explícitas em vez de derivados do nome de uma classe C#.
/// </para>
/// <para>
/// Renomear uma classe de consumidor cujo nome de fila fosse gerado por
/// convenção faria o serviço passar a escutar uma fila nova e vazia, enquanto a
/// antiga acumula mensagens órfãs. Constantes tornam a topologia imune a
/// refatorações de código.
/// </para>
/// <para>
/// A convenção é <c>&lt;serviço-dono&gt;.&lt;assunto&gt;</c>, o que deixa
/// evidente, ao olhar a interface de gerenciamento do RabbitMQ, qual serviço é
/// responsável por drenar cada fila.
/// </para>
/// </remarks>
public static class MessagingConstants
{
    /// <summary>Persistência de mensagens (consumida pelo Message Service).</summary>
    public const string ChatPersistQueue = "chat.persist";

    /// <summary>Projeção do read model de mensagens (interna ao Message Service).</summary>
    public const string MessageProjectionQueue = "message.projection";

    /// <summary>Entrada em conversa, na visão do Message Service.</summary>
    public const string MessageConversationJoinedQueue = "message.conversation-joined";

    /// <summary>Saída de conversa, na visão do Message Service.</summary>
    public const string MessageConversationLeftQueue = "message.conversation-left";

    /// <summary>Notificação de mensagem para usuários offline.</summary>
    public const string NotificationQueue = "notification.message-sent";

    /// <summary>Entrada em conversa, na visão do Notification Service.</summary>
    public const string NotificationConversationJoinedQueue = "notification.conversation-joined";

    /// <summary>Saída de conversa, na visão do Notification Service.</summary>
    public const string NotificationConversationLeftQueue = "notification.conversation-left";

    /// <summary>Usuário ficou online.</summary>
    public const string PresenceOnlineQueue = "presence.online";

    /// <summary>Usuário ficou offline.</summary>
    public const string PresenceOfflineQueue = "presence.offline";

    /// <summary>Usuário criado (consumida por quem precisa do diretório).</summary>
    public const string UserCreatedQueue = "identity.user-created";

    /// <summary>
    /// Exchange de dead-letter comum a todas as filas.
    /// </summary>
    /// <remarks>
    /// Mensagens que esgotaram as tentativas de processamento vão para cá em vez
    /// de serem descartadas. Sem isso, um bug de desserialização apagaria
    /// mensagens de usuários sem deixar rastro.
    /// </remarks>
    public const string DeadLetterExchange = "chat.dlx";
}
