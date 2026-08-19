using BuildingBlocks.Application;
using BuildingBlocks.Contracts;
using ChatService.Application.Abstractions;
using FluentValidation;
using MediatR;

namespace ChatService.Application.Conversations;

/// <summary>Comando de entrada numa sala de tempo real.</summary>
public sealed record JoinConversationCommand(Guid ConversationId, Guid UserId, string ConnectionId) : IRequest;

/// <summary>Regras de entrada.</summary>
public sealed class JoinConversationCommandValidator : AbstractValidator<JoinConversationCommand>
{
    /// <summary>Configura as regras.</summary>
    public JoinConversationCommandValidator()
    {
        RuleFor(command => command.ConversationId).NotEmpty();
        RuleFor(command => command.UserId).NotEmpty();
        RuleFor(command => command.ConnectionId).NotEmpty();
    }
}

/// <summary>
/// Inscreve a conexão na sala da conversa, após confirmar a participação.
/// </summary>
/// <remarks>
/// <b>É o ponto de controle mais crítico do serviço.</b> Uma vez inscrita no
/// grupo SignalR, a conexão passa a receber <i>tudo</i> o que for transmitido
/// naquela conversa, sem nova verificação por mensagem. Autorizar corretamente
/// aqui é o que impede que um intruso se torne um ouvinte silencioso e
/// permanente de uma conversa privada.
/// </remarks>
public sealed class JoinConversationCommandHandler(
    IConversationAccessPolicy accessPolicy,
    IConversationNotifier notifier,
    IChatEventPublisher publisher,
    IClock clock,
    IChatTelemetry telemetry)
    : IRequestHandler<JoinConversationCommand>
{
    /// <inheritdoc />
    public async Task Handle(JoinConversationCommand request, CancellationToken cancellationToken)
    {
        var canAccess = await accessPolicy.CanAccessConversationAsync(
            request.ConversationId,
            request.UserId,
            cancellationToken);

        if (!canAccess)
        {
            telemetry.AccessDenied(nameof(JoinConversationCommand));
            throw new ForbiddenException("Você não participa desta conversa.");
        }

        await notifier.AddConnectionToConversationAsync(
            request.ConnectionId,
            request.ConversationId,
            cancellationToken);

        await publisher.PublishAsync(
            new ConversationJoinedEvent(Guid.NewGuid(), clock.UtcNow, request.ConversationId, request.UserId),
            cancellationToken);

        telemetry.IncrementCommand(nameof(JoinConversationCommand));
    }
}
