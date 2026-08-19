using BuildingBlocks.Application;
using BuildingBlocks.Contracts;
using ChatService.Application.Abstractions;
using ChatService.Application.Contracts;
using FluentValidation;
using MediatR;

namespace ChatService.Application.Messages;

/// <summary>
/// Comando de envio de mensagem numa conversa.
/// </summary>
/// <param name="ConversationId">Conversa de destino.</param>
/// <param name="UserId">Remetente, sempre derivado do token JWT.</param>
/// <param name="SenderName">Nome de exibição do remetente, vindo do token.</param>
/// <param name="Content">Texto da mensagem.</param>
public sealed record SendMessageCommand(
    Guid ConversationId,
    Guid UserId,
    string SenderName,
    string Content) : IRequest<ChatRealtimeMessage>;

/// <summary>Regras de formato do envio.</summary>
public sealed class SendMessageCommandValidator : AbstractValidator<SendMessageCommand>
{
    /// <summary>Configura as regras.</summary>
    public SendMessageCommandValidator()
    {
        RuleFor(command => command.ConversationId).NotEmpty();
        RuleFor(command => command.UserId).NotEmpty();
        RuleFor(command => command.SenderName).NotEmpty().MaximumLength(256);

        RuleFor(command => command.Content)
            .NotEmpty().WithMessage("A mensagem não pode ser vazia.")
            // O limite espelha o `HasMaxLength(4000)` da tabela de mensagens.
            // Sem ele, uma mensagem maior seria transmitida em tempo real com
            // sucesso e depois falharia silenciosamente na persistência — o
            // usuário veria a mensagem enviada e ela sumiria ao recarregar. É o
            // tipo de bug que consome dias de investigação.
            .MaximumLength(4000).WithMessage("A mensagem excede o limite de 4000 caracteres.");
    }
}

/// <summary>
/// Verifica a autorização, transmite a mensagem em tempo real e publica o evento
/// de persistência.
/// </summary>
public sealed class SendMessageCommandHandler(
    IConversationAccessPolicy accessPolicy,
    IChatEventPublisher publisher,
    IConversationNotifier notifier,
    IClock clock,
    IChatTelemetry telemetry)
    : IRequestHandler<SendMessageCommand, ChatRealtimeMessage>
{
    /// <inheritdoc />
    public async Task<ChatRealtimeMessage> Handle(SendMessageCommand request, CancellationToken cancellationToken)
    {
        // ===== VERIFICAÇÃO DE AUTORIZAÇÃO =====
        //
        // Ausente na versão original. Sem ela, qualquer usuário autenticado podia
        // injetar mensagens em qualquer conversa apenas informando o
        // identificador — que não é secreto, aparece nas respostas da API.
        //
        // A checagem vem ANTES de qualquer efeito colateral: nada é transmitido
        // nem publicado se o acesso for negado.
        var canAccess = await accessPolicy.CanAccessConversationAsync(
            request.ConversationId,
            request.UserId,
            cancellationToken);

        if (!canAccess)
        {
            telemetry.AccessDenied(nameof(SendMessageCommand));
            throw new ForbiddenException("Você não participa desta conversa.");
        }

        var createdAtUtc = clock.UtcNow;
        var messageId = Guid.NewGuid();

        var content = request.Content.Trim();
        var senderName = request.SenderName.Trim();

        var message = new ChatRealtimeMessage(
            messageId,
            request.ConversationId,
            request.UserId,
            senderName,
            content,
            createdAtUtc);

        // ===== ORDEM DAS DUAS OPERAÇÕES SEGUINTES =====
        //
        // Publicar no broker ANTES de transmitir em tempo real é deliberado.
        //
        // Se a publicação falhar (RabbitMQ fora do ar), a exceção sobe e o
        // usuário recebe um erro — sem ter visto a mensagem aparecer na tela.
        // Na ordem inversa, ele veria a mensagem enviada e ela desapareceria no
        // próximo carregamento, porque nunca foi persistida. Falhar de forma
        // visível é melhor do que perder dado em silêncio.
        //
        // Vale registrar a limitação: não há transação entre publicar e
        // transmitir. Se o processo cair entre as duas linhas, a mensagem é
        // persistida mas não chega em tempo real — e o destinatário só a verá ao
        // recarregar. É um compromisso aceitável para chat; o padrão Outbox
        // (usado nos serviços com banco) seria a solução completa, mas exigiria
        // dar um banco de dados a este serviço, que hoje é sem estado.
        await publisher.PublishAsync(
            new MessageSentEvent(
                EventId: Guid.NewGuid(),
                OccurredAtUtc: createdAtUtc,
                MessageId: messageId,
                ConversationId: request.ConversationId,
                SenderId: request.UserId,
                SenderName: senderName,
                Content: content),
            cancellationToken);

        await notifier.BroadcastMessageAsync(request.ConversationId, message, cancellationToken);

        telemetry.IncrementCommand(nameof(SendMessageCommand));

        return message;
    }
}
