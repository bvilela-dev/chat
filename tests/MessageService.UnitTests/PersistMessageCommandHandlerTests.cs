using BuildingBlocks.Contracts;
using MessageService.Application.Abstractions;
using MessageService.Application.Contracts;
using MessageService.Application.Messages;
using MessageService.Domain;
using MessageService.UnitTests.TestDoubles;

namespace MessageService.UnitTests;

/// <summary>
/// Testes da persistência de mensagens vindas do barramento.
/// </summary>
/// <remarks>
/// O foco é <b>idempotência</b>. Como a entrega do RabbitMQ é "pelo menos uma
/// vez", este handler será inevitavelmente chamado duas vezes com o mesmo evento
/// — e a segunda chamada não pode duplicar a mensagem no histórico do usuário.
/// </remarks>
public sealed class PersistMessageCommandHandlerTests
{
    private readonly IMessageRepository _repository = Substitute.For<IMessageRepository>();
    private readonly IMessageTelemetry _telemetry = Substitute.For<IMessageTelemetry>();
    private readonly FixedClock _clock = FixedClock.Default();

    private static readonly Guid MessageId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ConversationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SenderId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private PersistMessageCommandHandler CreateHandler() => new(_repository, _clock, _telemetry);

    private static MessageSentEvent CreateEvent()
    {
        return new MessageSentEvent(
            EventId: Guid.NewGuid(),
            OccurredAtUtc: FixedClock.DefaultInstant,
            MessageId: MessageId,
            ConversationId: ConversationId,
            SenderId: SenderId,
            SenderName: "Bruno",
            Content: "Olá!");
    }

    [Fact]
    public async Task Deve_ignorar_um_evento_cuja_mensagem_ja_foi_persistida()
    {
        // IDEMPOTÊNCIA — a propriedade central deste handler.
        _repository.MessageExistsAsync(MessageId, Arg.Any<CancellationToken>()).Returns(true);

        await CreateHandler().Handle(new PersistMessageCommand(CreateEvent()), CancellationToken.None);

        // Nenhuma escrita: nem mensagem duplicada, nem projeção duplicada.
        _repository.DidNotReceive().AddMessage(Arg.Any<Message>());
        _repository.DidNotReceive().EnqueueOutbox(Arg.Any<IIntegrationEvent>());
        await _repository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deve_persistir_a_mensagem_quando_ela_ainda_nao_existe()
    {
        _repository.MessageExistsAsync(MessageId, Arg.Any<CancellationToken>()).Returns(false);
        _repository.GetConversationAsync(ConversationId, Arg.Any<CancellationToken>())
            .Returns(Conversation.Create(ConversationId, false, FixedClock.DefaultInstant));

        Message? mensagemPersistida = null;
        _repository.When(repository => repository.AddMessage(Arg.Any<Message>()))
            .Do(call => mensagemPersistida = call.Arg<Message>());

        await CreateHandler().Handle(new PersistMessageCommand(CreateEvent()), CancellationToken.None);

        mensagemPersistida.ShouldNotBeNull();
        mensagemPersistida.Id.ShouldBe(MessageId);
        mensagemPersistida.Content.ShouldBe("Olá!");
    }

    [Fact]
    public async Task Deve_criar_a_conversa_quando_ela_ainda_nao_existe()
    {
        // Cenário real: a mensagem chega antes de o comando de criação de
        // conversa ter sido processado — são caminhos assíncronos independentes.
        // Criar sob demanda evita perder a mensagem por uma corrida entre fluxos.
        _repository.MessageExistsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        _repository.GetConversationAsync(ConversationId, Arg.Any<CancellationToken>()).Returns((Conversation?)null);

        await CreateHandler().Handle(new PersistMessageCommand(CreateEvent()), CancellationToken.None);

        await _repository.Received(1).AddConversationAsync(
            Arg.Is<Conversation>(conversation => conversation.Id == ConversationId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deve_registrar_o_remetente_como_participante()
    {
        // Sem isso, a conversa criada implicitamente não apareceria na lista do
        // remetente — e ele não conseguiria mais ler o próprio histórico, já que
        // a leitura passou a exigir participação.
        _repository.MessageExistsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        _repository.GetConversationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Conversation.Create(ConversationId, false, FixedClock.DefaultInstant));

        await CreateHandler().Handle(new PersistMessageCommand(CreateEvent()), CancellationToken.None);

        await _repository.Received(1).AddParticipantAsync(
            ConversationId, SenderId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deve_enfileirar_a_projecao_na_mesma_transacao_da_gravacao()
    {
        _repository.MessageExistsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        _repository.GetConversationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Conversation.Create(ConversationId, false, FixedClock.DefaultInstant));

        IIntegrationEvent? projecao = null;
        _repository.When(repository => repository.EnqueueOutbox(Arg.Any<IIntegrationEvent>()))
            .Do(call => projecao = call.Arg<IIntegrationEvent>());

        await CreateHandler().Handle(new PersistMessageCommand(CreateEvent()), CancellationToken.None);

        // Nenhuma mensagem pode ser gravada sem que a projeção seja solicitada:
        // ela existiria no banco e nunca apareceria no histórico lido pela
        // interface. Um único SaveChanges garante a atomicidade dos dois.
        projecao.ShouldBeOfType<MessageProjectionRequested>();
        ((MessageProjectionRequested)projecao).MessageId.ShouldBe(MessageId);
        await _repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deve_preservar_o_instante_original_do_evento_na_mensagem()
    {
        // O timestamp da mensagem é o do FATO (quando o usuário enviou), não o do
        // processamento. Usar o instante do consumo faria a ordem cronológica do
        // histórico depender de atrasos da fila.
        var instanteDoEnvio = FixedClock.DefaultInstant.AddMinutes(-5);

        _repository.MessageExistsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        _repository.GetConversationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Conversation.Create(ConversationId, false, instanteDoEnvio));

        Message? mensagemPersistida = null;
        _repository.When(repository => repository.AddMessage(Arg.Any<Message>()))
            .Do(call => mensagemPersistida = call.Arg<Message>());

        var evento = CreateEvent() with { OccurredAtUtc = instanteDoEnvio };

        await CreateHandler().Handle(new PersistMessageCommand(evento), CancellationToken.None);

        mensagemPersistida.ShouldNotBeNull();
        mensagemPersistida.CreatedAtUtc.ShouldBe(instanteDoEnvio);
    }
}
