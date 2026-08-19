using BuildingBlocks.Application;
using BuildingBlocks.Contracts;
using MediatR;
using MessageService.Application.Abstractions;
using MessageService.Application.Contracts;
using MessageService.Domain;

namespace MessageService.Application.Messages;

/// <summary>
/// Comando que persiste uma mensagem recebida via evento de integração.
/// </summary>
/// <remarks>
/// Disparado pelo consumidor de <see cref="MessageSentEvent"/>. É o ponto em que
/// a mensagem, que já foi entregue em tempo real pelo SignalR, ganha
/// durabilidade.
/// </remarks>
public sealed record PersistMessageCommand(MessageSentEvent IntegrationEvent) : IRequest;

/// <summary>Grava a mensagem e enfileira a atualização das projeções.</summary>
public sealed class PersistMessageCommandHandler(
    IMessageRepository repository,
    IClock clock,
    IMessageTelemetry telemetry)
    : IRequestHandler<PersistMessageCommand>
{
    /// <inheritdoc />
    public async Task Handle(PersistMessageCommand request, CancellationToken cancellationToken)
    {
        var integrationEvent = request.IntegrationEvent;

        // PRIMEIRA CAMADA DE IDEMPOTÊNCIA: deduplicação pelo próprio dado.
        //
        // A tabela de inbox já barra o reprocessamento do mesmo EventId, mas esta
        // checagem cobre um caso adicional: o mesmo MessageId chegando por um
        // evento com EventId diferente (por exemplo, se o produtor for reiniciado
        // e republicar). Verificar contra o estado real do banco é a defesa mais
        // robusta — não depende de nenhum registro auxiliar estar correto.
        if (await repository.MessageExistsAsync(integrationEvent.MessageId, cancellationToken))
        {
            return;
        }

        // Cria a conversa se ela ainda não existir.
        //
        // Acontece quando a mensagem chega antes de o comando de criação de
        // conversa ter sido processado — perfeitamente possível, já que são
        // caminhos assíncronos independentes. Criar sob demanda evita perder a
        // mensagem por causa de uma corrida entre dois fluxos.
        var conversation = await repository.GetConversationAsync(integrationEvent.ConversationId, cancellationToken);
        if (conversation is null)
        {
            await repository.AddConversationAsync(
                Conversation.Create(integrationEvent.ConversationId, isGroup: false, integrationEvent.OccurredAtUtc),
                cancellationToken);
        }

        repository.AddMessage(Message.Create(
            integrationEvent.MessageId,
            integrationEvent.ConversationId,
            integrationEvent.SenderId,
            integrationEvent.SenderName,
            integrationEvent.Content,
            integrationEvent.OccurredAtUtc));

        // Quem enviou necessariamente participa da conversa. Registrar aqui
        // garante que a conversa apareça na lista do remetente mesmo quando ela
        // nasceu pelo caminho implícito acima.
        await repository.AddParticipantAsync(
            integrationEvent.ConversationId,
            integrationEvent.SenderId,
            integrationEvent.OccurredAtUtc,
            cancellationToken);

        // Enfileira a atualização das projeções na MESMA transação da gravação.
        // Nenhuma mensagem pode ser persistida sem que a projeção correspondente
        // seja solicitada — caso contrário ela existiria no banco mas nunca
        // apareceria no histórico lido pela interface.
        repository.EnqueueOutbox(new MessageProjectionRequested(
            EventId: Guid.NewGuid(),
            OccurredAtUtc: clock.UtcNow,
            MessageId: integrationEvent.MessageId,
            ConversationId: integrationEvent.ConversationId,
            SenderId: integrationEvent.SenderId,
            SenderName: integrationEvent.SenderName,
            Content: integrationEvent.Content,
            MessageCreatedAtUtc: integrationEvent.OccurredAtUtc));

        telemetry.RecordConsumedEvent(nameof(MessageSentEvent));

        await repository.SaveChangesAsync(cancellationToken);
    }
}
