using BuildingBlocks.Application;
using BuildingBlocks.Contracts;
using ChatService.Application.Abstractions;
using ChatService.Application.Conversations;
using ChatService.UnitTests.TestDoubles;

namespace ChatService.UnitTests;

/// <summary>
/// Testes de entrada e saída de salas de tempo real.
/// </summary>
public sealed class JoinConversationCommandHandlerTests
{
    private readonly IConversationAccessPolicy _accessPolicy = Substitute.For<IConversationAccessPolicy>();
    private readonly IConversationNotifier _notifier = Substitute.For<IConversationNotifier>();
    private readonly IChatEventPublisher _publisher = Substitute.For<IChatEventPublisher>();
    private readonly IChatTelemetry _telemetry = Substitute.For<IChatTelemetry>();
    private readonly FixedClock _clock = FixedClock.Default();

    private static readonly Guid ConversationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string ConnectionId = "conexao-abc";

    private JoinConversationCommandHandler CreateHandler()
    {
        return new JoinConversationCommandHandler(_accessPolicy, _notifier, _publisher, _clock, _telemetry);
    }

    [Fact]
    public async Task Deve_bloquear_a_entrada_de_quem_nao_participa_da_conversa()
    {
        // O PONTO DE CONTROLE MAIS CRÍTICO DO SERVIÇO.
        //
        // Uma vez dentro do grupo SignalR, a conexão recebe TUDO o que for
        // transmitido na conversa, sem nova verificação por mensagem. Autorizar
        // errado aqui transforma um intruso num ouvinte silencioso e permanente
        // de uma conversa privada.
        _accessPolicy
            .CanAccessConversationAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(false);

        await Should.ThrowAsync<ForbiddenException>(() => CreateHandler().Handle(
            new JoinConversationCommand(ConversationId, UserId, ConnectionId),
            CancellationToken.None));

        // A conexão NÃO pode ter sido inscrita no grupo.
        await _notifier.DidNotReceive().AddConnectionToConversationAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());

        _telemetry.Received(1).AccessDenied(nameof(JoinConversationCommand));
    }

    [Fact]
    public async Task Deve_inscrever_a_conexao_quando_o_usuario_participa()
    {
        _accessPolicy
            .CanAccessConversationAsync(ConversationId, UserId, Arg.Any<CancellationToken>())
            .Returns(true);

        await CreateHandler().Handle(
            new JoinConversationCommand(ConversationId, UserId, ConnectionId),
            CancellationToken.None);

        await _notifier.Received(1).AddConnectionToConversationAsync(
            ConnectionId, ConversationId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deve_publicar_o_evento_de_entrada_na_conversa()
    {
        _accessPolicy
            .CanAccessConversationAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(true);

        ConversationJoinedEvent? evento = null;
        await _publisher.PublishAsync(
            Arg.Do<ConversationJoinedEvent>(published => evento = published),
            Arg.Any<CancellationToken>());

        await CreateHandler().Handle(
            new JoinConversationCommand(ConversationId, UserId, ConnectionId),
            CancellationToken.None);

        // O evento é o que mantém as projeções de participação do Message Service
        // e do Notification Service em dia.
        evento.ShouldNotBeNull();
        evento.ConversationId.ShouldBe(ConversationId);
        evento.UserId.ShouldBe(UserId);
        evento.OccurredAtUtc.ShouldBe(FixedClock.DefaultInstant);
    }
}

/// <summary>Testes da saída de salas de tempo real.</summary>
public sealed class LeaveConversationCommandHandlerTests
{
    private readonly IConversationNotifier _notifier = Substitute.For<IConversationNotifier>();
    private readonly IChatEventPublisher _publisher = Substitute.For<IChatEventPublisher>();
    private readonly IChatTelemetry _telemetry = Substitute.For<IChatTelemetry>();
    private readonly FixedClock _clock = FixedClock.Default();

    [Fact]
    public async Task Deve_permitir_a_saida_sem_verificar_autorizacao()
    {
        // ASSIMETRIA DELIBERADA em relação à entrada.
        //
        // Sair reduz privilégio, nunca amplia. Exigir permissão para sair criaria
        // a situação absurda de alguém ficar preso numa sala por não conseguir
        // provar que pertence a ela. O pior caso de uma chamada indevida é
        // remover a própria conexão de um grupo em que não estava — efeito nulo.
        //
        // Repare que o handler sequer recebe IConversationAccessPolicy: a
        // ausência da dependência torna a decisão explícita no próprio construtor.
        var handler = new LeaveConversationCommandHandler(_notifier, _publisher, _clock, _telemetry);

        var conversationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await handler.Handle(
            new LeaveConversationCommand(conversationId, userId, "conexao-abc"),
            CancellationToken.None);

        await _notifier.Received(1).RemoveConnectionFromConversationAsync(
            "conexao-abc", conversationId, Arg.Any<CancellationToken>());

        await _publisher.Received(1).PublishAsync(
            Arg.Any<ConversationLeftEvent>(), Arg.Any<CancellationToken>());
    }
}
