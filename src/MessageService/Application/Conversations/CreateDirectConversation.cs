using BuildingBlocks.Application;
using FluentValidation;
using MediatR;
using MessageService.Application.Abstractions;
using MessageService.Application.Contracts;

namespace MessageService.Application.Conversations;

/// <summary>
/// Comando que abre (ou reaproveita) a conversa direta entre dois usuários.
/// </summary>
/// <param name="InitiatorId">Usuário que iniciou, derivado do token.</param>
/// <param name="ParticipantId">Usuário com quem se quer conversar.</param>
public sealed record CreateDirectConversationCommand(Guid InitiatorId, Guid ParticipantId)
    : IRequest<ConversationReadDto>;

/// <summary>Regras de criação de conversa direta.</summary>
public sealed class CreateDirectConversationCommandValidator : AbstractValidator<CreateDirectConversationCommand>
{
    /// <summary>Configura as regras.</summary>
    public CreateDirectConversationCommandValidator()
    {
        RuleFor(command => command.InitiatorId).NotEmpty();
        RuleFor(command => command.ParticipantId).NotEmpty();

        RuleFor(command => command)
            .Must(command => command.InitiatorId != command.ParticipantId)
            .WithMessage("Não é possível iniciar uma conversa direta consigo mesmo.");
    }
}

/// <summary>
/// Cria a conversa direta, ou devolve a existente.
/// </summary>
/// <remarks>
/// A operação é <b>idempotente do ponto de vista do usuário</b>: clicar duas
/// vezes no mesmo contato leva à mesma conversa, e não a duas. É o comportamento
/// esperado em qualquer aplicativo de mensagens.
/// </remarks>
public sealed class CreateDirectConversationCommandHandler(IMessageRepository repository, IClock clock)
    : IRequestHandler<CreateDirectConversationCommand, ConversationReadDto>
{
    /// <inheritdoc />
    public async Task<ConversationReadDto> Handle(
        CreateDirectConversationCommand request,
        CancellationToken cancellationToken)
    {
        var utcNow = clock.UtcNow;

        var existingConversation = await repository.GetDirectConversationAsync(
            request.InitiatorId,
            request.ParticipantId,
            cancellationToken);

        if (existingConversation is not null)
        {
            // Reforça a participação dos dois usuários antes de devolver.
            //
            // Não é redundante: a conversa pode ter sido criada implicitamente ao
            // persistir uma mensagem (caminho em que só o remetente vira
            // participante). Garantir os dois vínculos aqui é o que faz a
            // conversa aparecer na lista de ambos os lados.
            await repository.AddParticipantAsync(existingConversation.Id, request.InitiatorId, utcNow, cancellationToken);
            await repository.AddParticipantAsync(existingConversation.Id, request.ParticipantId, utcNow, cancellationToken);
            await repository.SaveChangesAsync(cancellationToken);

            return existingConversation.ToDto();
        }

        var conversationId = Guid.NewGuid();

        await repository.CreateDirectConversationAsync(
            conversationId,
            request.InitiatorId,
            request.ParticipantId,
            utcNow,
            cancellationToken);

        await repository.SaveChangesAsync(cancellationToken);

        // Conversa recém-criada: sem mensagens, sem data de última atividade.
        return new ConversationReadDto(
            conversationId,
            LastMessage: string.Empty,
            LastMessageAtUtc: null,
            IsGroup: false,
            CounterpartUserId: request.ParticipantId);
    }
}
