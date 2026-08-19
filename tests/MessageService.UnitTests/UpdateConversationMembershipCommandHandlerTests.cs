using MessageService.Application.Abstractions;
using MessageService.Application.Conversations;
using MessageService.UnitTests.TestDoubles;

namespace MessageService.UnitTests;

/// <summary>
/// Testes da sincronização de participação a partir dos eventos do Chat Service.
/// </summary>
public sealed class UpdateConversationMembershipCommandHandlerTests
{
    private readonly IMessageRepository _repository = Substitute.For<IMessageRepository>();
    private readonly IMessageTelemetry _telemetry = Substitute.For<IMessageTelemetry>();
    private readonly FixedClock _clock = FixedClock.Default();

    private static readonly Guid ConversationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private UpdateConversationMembershipCommandHandler CreateHandler() => new(_repository, _clock, _telemetry);

    [Fact]
    public async Task Deve_adicionar_o_participante_no_evento_de_entrada()
    {
        await CreateHandler().Handle(
            new UpdateConversationMembershipCommand(ConversationId, UserId, Joined: true),
            CancellationToken.None);

        await _repository.Received(1).AddParticipantAsync(
            ConversationId, UserId, FixedClock.DefaultInstant, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deve_remover_o_participante_no_evento_de_saida()
    {
        // TESTE DE REGRESSÃO DE UM BUG REAL.
        //
        // A versão anterior tinha apenas o ramo `if (request.Joined)`, sem
        // `else`. O evento de saída era consumido, marcado como processado — e
        // nada acontecia. A conversa permanecia na lista do usuário para sempre,
        // e ele continuava sendo tratado como participante na autorização.
        await CreateHandler().Handle(
            new UpdateConversationMembershipCommand(ConversationId, UserId, Joined: false),
            CancellationToken.None);

        await _repository.Received(1).RemoveParticipantAsync(
            ConversationId, UserId, Arg.Any<CancellationToken>());

        await _repository.DidNotReceive().AddParticipantAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deve_persistir_a_alteracao()
    {
        await CreateHandler().Handle(
            new UpdateConversationMembershipCommand(ConversationId, UserId, Joined: true),
            CancellationToken.None);

        await _repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
