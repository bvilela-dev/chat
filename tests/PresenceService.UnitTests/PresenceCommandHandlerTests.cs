using BuildingBlocks.Contracts;
using PresenceService.Application.Abstractions;
using PresenceService.Application.Presence;
using PresenceService.Domain;
using PresenceService.UnitTests.TestDoubles;

namespace PresenceService.UnitTests;

/// <summary>
/// Testes dos comandos de presença.
/// </summary>
public sealed class PresenceCommandHandlerTests
{
    private readonly IPresenceStore _store = Substitute.For<IPresenceStore>();
    private readonly IPresenceEventPublisher _publisher = Substitute.For<IPresenceEventPublisher>();
    private readonly IPresenceTelemetry _telemetry = Substitute.For<IPresenceTelemetry>();
    private readonly FixedClock _clock = FixedClock.Default();

    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Deve_marcar_o_usuario_como_online_e_publicar_o_evento()
    {
        _store.SetOnlineAsync(UserId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new UserPresence(UserId, true, FixedClock.DefaultInstant));

        var handler = new SetUserOnlineCommandHandler(_store, _publisher, _clock, _telemetry);

        var status = await handler.Handle(new SetUserOnlineCommand(UserId), CancellationToken.None);

        status.IsOnline.ShouldBeTrue();
        await _publisher.Received(1).PublishAsync(Arg.Any<UserOnlineEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deve_marcar_o_usuario_como_offline_e_publicar_o_evento()
    {
        _store.SetOfflineAsync(UserId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new UserPresence(UserId, false, FixedClock.DefaultInstant));

        var handler = new SetUserOfflineCommandHandler(_store, _publisher, _clock, _telemetry);

        var status = await handler.Handle(new SetUserOfflineCommand(UserId), CancellationToken.None);

        status.IsOnline.ShouldBeFalse();
        await _publisher.Received(1).PublishAsync(Arg.Any<UserOfflineEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deve_usar_o_mesmo_instante_no_estado_e_no_evento_de_saida()
    {
        // Sem isso, "ficou offline" e "visto por último" divergiriam por alguns
        // milissegundos sem nenhum motivo — e a divergência apareceria em
        // qualquer painel que cruzasse os dois valores.
        _store.SetOfflineAsync(UserId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new UserPresence(UserId, false, FixedClock.DefaultInstant));

        UserOfflineEvent? evento = null;
        await _publisher.PublishAsync(
            Arg.Do<UserOfflineEvent>(published => evento = published),
            Arg.Any<CancellationToken>());

        var handler = new SetUserOfflineCommandHandler(_store, _publisher, _clock, _telemetry);
        await handler.Handle(new SetUserOfflineCommand(UserId), CancellationToken.None);

        evento.ShouldNotBeNull();
        evento.OccurredAtUtc.ShouldBe(FixedClock.DefaultInstant);
        evento.LastSeenAtUtc.ShouldBe(FixedClock.DefaultInstant);

        await _store.Received(1).SetOfflineAsync(UserId, FixedClock.DefaultInstant, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deve_listar_apenas_os_usuarios_online()
    {
        var primeiro = Guid.NewGuid();
        var segundo = Guid.NewGuid();

        _store.GetOnlineAsync(Arg.Any<CancellationToken>()).Returns([
            new UserPresence(primeiro, true, null),
            new UserPresence(segundo, true, null)
        ]);

        var handler = new GetOnlineUsersQueryHandler(_store);

        var resultado = await handler.Handle(new GetOnlineUsersQuery(), CancellationToken.None);

        resultado.Count.ShouldBe(2);
        resultado.ShouldAllBe(status => status.IsOnline);
    }
}

/// <summary>Testes dos validadores de presença.</summary>
public sealed class PresenceValidatorTests
{
    [Fact]
    public void Deve_recusar_comando_sem_identificador_de_usuario()
    {
        new SetUserOnlineCommandValidator()
            .Validate(new SetUserOnlineCommand(Guid.Empty))
            .IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Deve_aceitar_comando_com_identificador_valido()
    {
        new SetUserOnlineCommandValidator()
            .Validate(new SetUserOnlineCommand(Guid.NewGuid()))
            .IsValid.ShouldBeTrue();
    }
}
