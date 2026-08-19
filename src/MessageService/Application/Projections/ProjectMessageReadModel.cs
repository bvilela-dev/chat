using BuildingBlocks.Application;
using MediatR;
using MessageService.Application.Abstractions;
using MessageService.Application.Contracts;

namespace MessageService.Application.Projections;

/// <summary>
/// Comando que aplica uma mensagem às projeções de leitura.
/// </summary>
public sealed record ProjectMessageReadModelCommand(MessageProjectionRequested Projection) : IRequest;

/// <summary>
/// Atualiza o read model da mensagem, o resumo da conversa e o vínculo do
/// participante.
/// </summary>
public sealed class ProjectMessageReadModelCommandHandler(
    IMessageRepository repository,
    IClock clock,
    IMessageTelemetry telemetry)
    : IRequestHandler<ProjectMessageReadModelCommand>
{
    /// <inheritdoc />
    public async Task Handle(ProjectMessageReadModelCommand request, CancellationToken cancellationToken)
    {
        await repository.UpsertProjectionAsync(request.Projection, cancellationToken);

        telemetry.RecordConsumedEvent(nameof(MessageProjectionRequested));

        // Mede a janela real de consistência eventual: quanto tempo se passou
        // entre o usuário enviar a mensagem e ela ficar visível no histórico.
        //
        // Este é o número a observar num painel de CQRS. Enquanto fica em
        // milissegundos, ninguém percebe a assincronia. Se subir para segundos,
        // o usuário envia uma mensagem, recarrega a página e não a encontra — e
        // reporta como "mensagem perdida".
        telemetry.RecordProjectionLag(clock.UtcNow - request.Projection.MessageCreatedAtUtc);

        await repository.SaveChangesAsync(cancellationToken);
    }
}
