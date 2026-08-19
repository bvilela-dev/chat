using BuildingBlocks.Contracts;
using ChatService.Application.Abstractions;
using MassTransit;

namespace ChatService.Infrastructure.Messaging;

/// <summary>
/// Publica eventos de integração no RabbitMQ através do MassTransit.
/// </summary>
/// <remarks>
/// Adaptador fino, mas com propósito: mantém <c>MassTransit</c> fora da camada de
/// aplicação. Trocar o barramento (por Azure Service Bus, Kafka ou uma
/// implementação em memória para testes) fica restrito a esta classe.
/// </remarks>
public sealed class ChatEventPublisher(IPublishEndpoint publishEndpoint) : IChatEventPublisher
{
    /// <inheritdoc />
    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : class, IIntegrationEvent
    {
        // Publish (e não Send): semântica de publish/subscribe. A mensagem vai
        // para uma exchange do tipo fanout e alcança TODOS os interessados —
        // hoje, Message Service e Notification Service. Um `Send` entregaria a
        // uma fila específica e exigiria que o produtor conhecesse os
        // consumidores, recriando o acoplamento que a arquitetura evita.
        return publishEndpoint.Publish(integrationEvent, cancellationToken);
    }
}
