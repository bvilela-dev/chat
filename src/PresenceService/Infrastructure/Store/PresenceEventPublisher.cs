using BuildingBlocks.Contracts;
using MassTransit;
using PresenceService.Application.Abstractions;

namespace PresenceService.Infrastructure.Store;

/// <summary>Publica eventos de presença no RabbitMQ.</summary>
public sealed class PresenceEventPublisher(IPublishEndpoint publishEndpoint) : IPresenceEventPublisher
{
    /// <inheritdoc />
    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : class, IIntegrationEvent
    {
        return publishEndpoint.Publish(integrationEvent, cancellationToken);
    }
}
