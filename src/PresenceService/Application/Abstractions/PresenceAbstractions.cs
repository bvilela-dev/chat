using BuildingBlocks.Contracts;
using PresenceService.Domain;

namespace PresenceService.Application.Abstractions;

/// <summary>
/// Armazenamento do estado de presença.
/// </summary>
public interface IPresenceStore
{
    /// <summary>
    /// Marca o usuário como online e renova a expiração do registro.
    /// </summary>
    /// <remarks>
    /// A operação é também o <i>heartbeat</i>: o cliente a chama periodicamente
    /// para sinalizar que continua ativo, e cada chamada empurra o TTL para a
    /// frente.
    /// </remarks>
    Task<UserPresence> SetOnlineAsync(Guid userId, DateTime occurredAtUtc, CancellationToken cancellationToken);

    /// <summary>Marca o usuário como offline e registra o último instante visto.</summary>
    Task<UserPresence> SetOfflineAsync(Guid userId, DateTime occurredAtUtc, CancellationToken cancellationToken);

    /// <summary>Consulta o estado de um usuário.</summary>
    Task<UserPresence> GetStatusAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Lista todos os usuários atualmente online.</summary>
    Task<IReadOnlyCollection<UserPresence>> GetOnlineAsync(CancellationToken cancellationToken);
}

/// <summary>Publicação de eventos de presença no barramento.</summary>
public interface IPresenceEventPublisher
{
    /// <summary>Publica o evento no RabbitMQ.</summary>
    Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : class, IIntegrationEvent;
}

/// <summary>Métricas do Presence Service.</summary>
public interface IPresenceTelemetry
{
    /// <summary>Contabiliza a execução de um comando.</summary>
    void RecordCommand(string commandName);
}
