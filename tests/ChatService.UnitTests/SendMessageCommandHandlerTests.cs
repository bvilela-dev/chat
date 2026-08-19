using BuildingBlocks.Application;
using BuildingBlocks.Contracts;
using ChatService.Application.Abstractions;
using ChatService.Application.Contracts;
using ChatService.Application.Messages;
using ChatService.UnitTests.TestDoubles;

namespace ChatService.UnitTests;

/// <summary>
/// Testes do envio de mensagem.
/// </summary>
/// <remarks>
/// <b>Esta é a suíte mais importante do projeto do ponto de vista de segurança.</b>
/// Ela trava a correção da falha mais grave encontrada: o handler original não
/// verificava se o remetente participava da conversa, permitindo que qualquer
/// usuário autenticado injetasse mensagens em qualquer conversa apenas
/// informando o identificador — que não é secreto.
/// </remarks>
public sealed class SendMessageCommandHandlerTests
{
    private readonly IConversationAccessPolicy _accessPolicy = Substitute.For<IConversationAccessPolicy>();
    private readonly IChatEventPublisher _publisher = Substitute.For<IChatEventPublisher>();
    private readonly IConversationNotifier _notifier = Substitute.For<IConversationNotifier>();
    private readonly IChatTelemetry _telemetry = Substitute.For<IChatTelemetry>();
    private readonly FixedClock _clock = FixedClock.Default();

    private static readonly Guid ConversationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private SendMessageCommandHandler CreateHandler()
    {
        return new SendMessageCommandHandler(_accessPolicy, _publisher, _notifier, _clock, _telemetry);
    }

    private void AllowAccess(bool allowed)
    {
        _accessPolicy
            .CanAccessConversationAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(allowed);
    }

    [Fact]
    public async Task Deve_bloquear_o_envio_quando_o_usuario_nao_participa_da_conversa()
    {
        // O TESTE DE REGRESSÃO DA FALHA DE AUTORIZAÇÃO.
        AllowAccess(false);

        await Should.ThrowAsync<ForbiddenException>(() => CreateHandler().Handle(
            new SendMessageCommand(ConversationId, UserId, "Intruso", "mensagem indevida"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Nao_deve_publicar_nem_transmitir_quando_o_acesso_e_negado()
    {
        // Não basta lançar a exceção: nada pode ter vazado antes disso. Se o
        // evento fosse publicado e só depois o acesso fosse recusado, a mensagem
        // seria persistida pelo Message Service assim mesmo — e a "proteção"
        // seria puramente cosmética.
        AllowAccess(false);

        await Should.ThrowAsync<ForbiddenException>(() => CreateHandler().Handle(
            new SendMessageCommand(ConversationId, UserId, "Intruso", "mensagem indevida"),
            CancellationToken.None));

        await _publisher.DidNotReceive().PublishAsync(Arg.Any<MessageSentEvent>(), Arg.Any<CancellationToken>());
        await _notifier.DidNotReceive().BroadcastMessageAsync(
            Arg.Any<Guid>(), Arg.Any<ChatRealtimeMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deve_registrar_metrica_de_acesso_negado()
    {
        // Observabilidade de segurança: um pico nesta métrica indica alguém
        // sondando identificadores de conversa. É o alerta que se quer receber.
        AllowAccess(false);

        await Should.ThrowAsync<ForbiddenException>(() => CreateHandler().Handle(
            new SendMessageCommand(ConversationId, UserId, "Intruso", "conteudo"),
            CancellationToken.None));

        _telemetry.Received(1).AccessDenied(nameof(SendMessageCommand));
    }

    [Fact]
    public async Task Deve_enviar_a_mensagem_quando_o_usuario_participa_da_conversa()
    {
        AllowAccess(true);

        var mensagem = await CreateHandler().Handle(
            new SendMessageCommand(ConversationId, UserId, "Bruno", "Olá!"),
            CancellationToken.None);

        mensagem.ConversationId.ShouldBe(ConversationId);
        mensagem.SenderId.ShouldBe(UserId);
        mensagem.SenderName.ShouldBe("Bruno");
        mensagem.Content.ShouldBe("Olá!");
        mensagem.CreatedAtUtc.ShouldBe(FixedClock.DefaultInstant);
    }

    [Fact]
    public async Task Deve_publicar_o_evento_antes_de_transmitir_em_tempo_real()
    {
        // ORDEM DAS OPERAÇÕES — decisão de projeto explícita.
        //
        // Publicar primeiro garante que, se o RabbitMQ estiver fora do ar, o
        // usuário receba um erro em vez de ver a mensagem aparecer na tela e
        // desaparecer no próximo carregamento (porque nunca foi persistida).
        // Falhar de forma visível é melhor do que perder dado em silêncio.
        AllowAccess(true);

        var ordemDasChamadas = new List<string>();

        _publisher
            .When(publisher => publisher.PublishAsync(Arg.Any<MessageSentEvent>(), Arg.Any<CancellationToken>()))
            .Do(_ => ordemDasChamadas.Add("publish"));

        _notifier
            .When(notifier => notifier.BroadcastMessageAsync(
                Arg.Any<Guid>(), Arg.Any<ChatRealtimeMessage>(), Arg.Any<CancellationToken>()))
            .Do(_ => ordemDasChamadas.Add("broadcast"));

        await CreateHandler().Handle(
            new SendMessageCommand(ConversationId, UserId, "Bruno", "Olá!"),
            CancellationToken.None);

        ordemDasChamadas.ShouldBe(["publish", "broadcast"]);
    }

    [Fact]
    public async Task Deve_publicar_o_evento_com_o_mesmo_identificador_da_mensagem_transmitida()
    {
        // O MessageId precisa ser o mesmo nos dois caminhos: é ele que permite ao
        // consumidor detectar reprocessamento, e ao cliente reconciliar a
        // mensagem otimista com a persistida.
        AllowAccess(true);

        MessageSentEvent? eventoPublicado = null;
        await _publisher.PublishAsync(
            Arg.Do<MessageSentEvent>(evento => eventoPublicado = evento),
            Arg.Any<CancellationToken>());

        var mensagem = await CreateHandler().Handle(
            new SendMessageCommand(ConversationId, UserId, "Bruno", "Olá!"),
            CancellationToken.None);

        eventoPublicado.ShouldNotBeNull();
        eventoPublicado.MessageId.ShouldBe(mensagem.MessageId);
        eventoPublicado.EventId.ShouldNotBe(eventoPublicado.MessageId);
    }

    [Fact]
    public async Task Deve_remover_espacos_das_pontas_do_conteudo_e_do_nome()
    {
        AllowAccess(true);

        var mensagem = await CreateHandler().Handle(
            new SendMessageCommand(ConversationId, UserId, "  Bruno  ", "  Olá!  "),
            CancellationToken.None);

        mensagem.Content.ShouldBe("Olá!");
        mensagem.SenderName.ShouldBe("Bruno");
    }

    [Fact]
    public async Task Deve_verificar_o_acesso_para_a_conversa_e_o_usuario_corretos()
    {
        AllowAccess(true);

        await CreateHandler().Handle(
            new SendMessageCommand(ConversationId, UserId, "Bruno", "Olá!"),
            CancellationToken.None);

        // Garante que a verificação não é decorativa: ela precisa consultar
        // exatamente o par (conversa, usuário) do comando.
        await _accessPolicy.Received(1).CanAccessConversationAsync(
            ConversationId, UserId, Arg.Any<CancellationToken>());
    }
}
