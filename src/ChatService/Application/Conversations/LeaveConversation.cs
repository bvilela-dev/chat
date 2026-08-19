using BuildingBlocks.Application;
using BuildingBlocks.Contracts;
using ChatService.Application.Abstractions;
using FluentValidation;
using MediatR;

namespace ChatService.Application.Conversations;

/// <summary>Comando de saída de uma sala de tempo real.</summary>
public sealed record LeaveConversationCommand(Guid ConversationId, Guid UserId, string ConnectionId) : IRequest;

/// <summary>Regras de saída.</summary>
public sealed class LeaveConversationCommandValidator : AbstractValidator<LeaveConversationCommand>
{
    /// <summary>Configura as regras.</summary>
    public LeaveConversationCommandValidator()
    {
        RuleFor(command => command.ConversationId).NotEmpty();
        RuleFor(command => command.UserId).NotEmpty();
        RuleFor(command => command.ConnectionId).NotEmpty();
    }
}

/// <summary>
/// Remove a inscrição da conexão na sala.
/// </summary>
/// <remarks>
/// <b>Sem verificação de autorização, de propósito.</b> Sair de uma conversa
/// reduz privilégio, nunca amplia. Exigir permissão para sair criaria a situação
/// absurda de um usuário ficar preso numa sala por não conseguir provar que
/// pertence a ela — e o pior caso de uma chamada indevida é alguém remover a
/// própria conexão de um grupo em que não estava, o que não tem efeito nenhum.
/// </remarks>
public sealed class LeaveConversationCommandHandler(
    IConversationNotifier notifier,
    IChatEventPublisher publisher,
    IClock clock,
    IChatTelemetry telemetry)
    : IRequestHandler<LeaveConversationCommand>
{
    /// <inheritdoc />
    public async Task Handle(LeaveConversationCommand request, CancellationToken cancellationToken)
    {
        await notifier.RemoveConnectionFromConversationAsync(
            request.ConnectionId,
            request.ConversationId,
            cancellationToken);

        await publisher.PublishAsync(
            new ConversationLeftEvent(Guid.NewGuid(), clock.UtcNow, request.ConversationId, request.UserId),
            cancellationToken);

        telemetry.IncrementCommand(nameof(LeaveConversationCommand));
    }
}
