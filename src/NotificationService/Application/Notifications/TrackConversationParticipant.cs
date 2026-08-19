using BuildingBlocks.Contracts;
using MediatR;
using NotificationService.Application.Abstractions;

namespace NotificationService.Application.Notifications;

/// <summary>
/// Comando que atualiza a projeção local de participantes de uma conversa.
/// </summary>
public sealed record TrackConversationParticipantCommand(
    Guid ConversationId,
    Guid UserId,
    bool Joined) : IRequest;

/// <summary>Aplica a entrada ou a saída de um participante na projeção local.</summary>
public sealed class TrackConversationParticipantCommandHandler(
    IConversationMembershipStore membershipStore,
    INotificationTelemetry telemetry)
    : IRequestHandler<TrackConversationParticipantCommand>
{
    /// <inheritdoc />
    public async Task Handle(TrackConversationParticipantCommand request, CancellationToken cancellationToken)
    {
        if (request.Joined)
        {
            await membershipStore.AddParticipantAsync(request.ConversationId, request.UserId, cancellationToken);
        }
        else
        {
            // CORREÇÃO: a versão anterior tratava apenas o caso "entrou". O
            // evento de saída era consumido e marcado como processado, mas o
            // participante nunca era removido da projeção.
            //
            // O efeito prático era que um usuário continuava recebendo
            // notificações de uma conversa da qual já havia saído — indefinidamente.
            await membershipStore.RemoveParticipantAsync(request.ConversationId, request.UserId, cancellationToken);
        }

        telemetry.RecordEvent(request.Joined
            ? nameof(ConversationJoinedEvent)
            : nameof(ConversationLeftEvent));
    }
}
