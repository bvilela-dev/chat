using BuildingBlocks.Application;
using BuildingBlocks.Contracts;
using MediatR;
using MessageService.Application.Abstractions;

namespace MessageService.Application.Conversations;

/// <summary>
/// Comando que sincroniza a participação numa conversa a partir dos eventos de
/// entrada e saída emitidos pelo Chat Service.
/// </summary>
public sealed record UpdateConversationMembershipCommand(
    Guid ConversationId,
    Guid UserId,
    bool Joined) : IRequest;

/// <summary>Aplica a entrada ou a saída de um participante.</summary>
public sealed class UpdateConversationMembershipCommandHandler(
    IMessageRepository repository,
    IClock clock,
    IMessageTelemetry telemetry)
    : IRequestHandler<UpdateConversationMembershipCommand>
{
    /// <inheritdoc />
    public async Task Handle(UpdateConversationMembershipCommand request, CancellationToken cancellationToken)
    {
        if (request.Joined)
        {
            // AddParticipantAsync já é idempotente: se o vínculo existe, não faz
            // nada. Isso é o que permite reprocessar o evento sem efeito colateral.
            await repository.AddParticipantAsync(
                request.ConversationId,
                request.UserId,
                clock.UtcNow,
                cancellationToken);
        }
        else
        {
            // CORREÇÃO: a versão anterior ignorava o evento de saída por completo
            // — o `else` simplesmente não existia. O usuário saía da conversa,
            // o evento era publicado, consumido, marcado como processado... e
            // nada acontecia. A conversa continuava na lista dele para sempre.
            await repository.RemoveParticipantAsync(
                request.ConversationId,
                request.UserId,
                cancellationToken);
        }

        telemetry.RecordConsumedEvent(request.Joined
            ? nameof(ConversationJoinedEvent)
            : nameof(ConversationLeftEvent));

        await repository.SaveChangesAsync(cancellationToken);
    }
}
