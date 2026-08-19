namespace NotificationService.Application.Abstractions;

/// <summary>
/// Registro de quais usuários participam de cada conversa, na visão deste serviço.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que este serviço mantém a própria cópia da participação.</b> Ele
/// precisa saber quem notificar quando uma mensagem chega. Poderia consultar o
/// Message Service a cada evento — mas isso criaria acoplamento síncrono num
/// caminho puramente assíncrono, e uma indisponibilidade daquele serviço
/// interromperia as notificações.
/// </para>
/// <para>
/// Em vez disso, ele constrói a própria projeção a partir dos eventos de entrada
/// e saída de conversa. É a aplicação do princípio de que <b>cada serviço mantém
/// os dados de que precisa, na forma de que precisa</b>. A duplicação é
/// intencional; a alternativa (um banco compartilhado) seria bem pior.
/// </para>
/// <para>
/// Diferentemente da checagem de autorização — que exige o dado atual e por isso
/// é síncrona —, aqui a consistência eventual é aceitável: notificar alguém que
/// acabou de sair da conversa é um incômodo pequeno, não uma falha de segurança.
/// </para>
/// </remarks>
public interface IConversationMembershipStore
{
    /// <summary>Registra um participante.</summary>
    Task AddParticipantAsync(Guid conversationId, Guid userId, CancellationToken cancellationToken);

    /// <summary>Remove um participante.</summary>
    Task RemoveParticipantAsync(Guid conversationId, Guid userId, CancellationToken cancellationToken);

    /// <summary>Lista os participantes de uma conversa.</summary>
    Task<IReadOnlyCollection<Guid>> GetParticipantsAsync(Guid conversationId, CancellationToken cancellationToken);

    /// <summary>Indica se este consumidor já processou o evento.</summary>
    Task<bool> HasProcessedAsync(Guid eventId, string consumerName, CancellationToken cancellationToken);

    /// <summary>Registra o processamento de um evento.</summary>
    Task MarkProcessedAsync(Guid eventId, string consumerName, CancellationToken cancellationToken);
}

/// <summary>Consulta do estado de presença de um usuário.</summary>
/// <remarks>
/// Lê diretamente do Redis compartilhado, escrito pelo Presence Service.
/// <para>
/// <b>Ressalva de arquitetura, registrada de forma honesta:</b> isto é
/// acoplamento por armazenamento — dois serviços compartilhando o mesmo formato
/// de chave. O desenho mais correto seria o Presence Service expor um endpoint
/// gRPC, ou este serviço manter a própria projeção a partir dos eventos
/// <c>UserOnlineEvent</c>/<c>UserOfflineEvent</c> que já são publicados.
/// </para>
/// <para>
/// A leitura direta foi mantida porque é uma consulta de altíssima frequência
/// (uma por participante, por mensagem) e o custo de uma chamada de rede aqui
/// seria desproporcional. A abstração desta interface é o que torna a troca
/// futura barata.
/// </para>
/// </remarks>
public interface IPresenceLookup
{
    /// <summary>Indica se o usuário está online no momento.</summary>
    Task<bool> IsOnlineAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>Envio efetivo de notificações.</summary>
/// <remarks>
/// A implementação atual apenas registra em log — é um <i>stub</i> explícito. A
/// abstração existe para que integrar Firebase Cloud Messaging, APNs ou SendGrid
/// seja trocar a implementação registrada no contêiner, sem alterar nenhuma
/// regra de negócio.
/// </remarks>
public interface INotificationSender
{
    /// <summary>Envia uma notificação push.</summary>
    Task SendPushAsync(Guid userId, string message, CancellationToken cancellationToken);

    /// <summary>Envia uma notificação por e-mail.</summary>
    Task SendEmailAsync(Guid userId, string subject, string message, CancellationToken cancellationToken);
}

/// <summary>Métricas do Notification Service.</summary>
public interface INotificationTelemetry
{
    /// <summary>Contabiliza um evento consumido.</summary>
    void RecordEvent(string eventName);

    /// <summary>Contabiliza uma notificação despachada.</summary>
    void RecordNotificationSent(string channel);
}
