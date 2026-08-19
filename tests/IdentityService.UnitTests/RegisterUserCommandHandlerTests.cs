using BuildingBlocks.Application;
using BuildingBlocks.Contracts;
using IdentityService.Application.Abstractions;
using IdentityService.Application.Contracts;
using IdentityService.Application.Users;
using IdentityService.Domain;
using IdentityService.UnitTests.TestDoubles;

namespace IdentityService.UnitTests;

/// <summary>
/// Testes do caso de uso de cadastro de usuário.
/// </summary>
/// <remarks>
/// Todas as dependências são substituídas por dublês. O handler é exercitado em
/// isolamento — sem PostgreSQL, sem RabbitMQ, sem BCrypt real (que custaria
/// ~250 ms por chamada e tornaria a suíte lenta).
/// </remarks>
public sealed class RegisterUserCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();
    private readonly IOutboxWriter _outboxWriter = Substitute.For<IOutboxWriter>();
    private readonly FixedClock _clock = FixedClock.Default();

    private RegisterUserCommandHandler CreateHandler()
    {
        return new RegisterUserCommandHandler(_userRepository, _passwordHasher, _tokenService, _outboxWriter, _clock);
    }

    private void ArrangeHappyPath()
    {
        _userRepository.EmailExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        _passwordHasher.Hash(Arg.Any<string>()).Returns("hash-derivado");
        _tokenService.CreateTokenPair(Arg.Any<User>(), Arg.Any<DateTime>()).Returns(
            new TokenPair(
                "access-token",
                FixedClock.DefaultInstant.AddMinutes(15),
                "refresh-token",
                FixedClock.DefaultInstant.AddDays(7)));
    }

    [Fact]
    public async Task Deve_cadastrar_o_usuario_e_devolver_os_tokens()
    {
        ArrangeHappyPath();

        var resposta = await CreateHandler().Handle(
            new RegisterUserCommand("Bruno", "bruno@teste.dev", "senha-segura-123"),
            CancellationToken.None);

        resposta.AccessToken.ShouldBe("access-token");
        resposta.RefreshToken.ShouldBe("refresh-token");
        resposta.User.Name.ShouldBe("Bruno");
        resposta.User.Email.ShouldBe("bruno@teste.dev");
    }

    [Fact]
    public async Task Nunca_deve_persistir_a_senha_em_texto_claro()
    {
        ArrangeHappyPath();
        User? usuarioPersistido = null;
        await _userRepository.AddAsync(
            Arg.Do<User>(user => usuarioPersistido = user),
            Arg.Any<CancellationToken>());

        await CreateHandler().Handle(
            new RegisterUserCommand("Bruno", "bruno@teste.dev", "senha-em-texto-claro"),
            CancellationToken.None);

        // A verificação mais importante deste arquivo. Uma refatoração que
        // acidentalmente gravasse `request.Password` em vez do hash passaria por
        // qualquer revisão de código distraída; aqui ela quebra o build.
        usuarioPersistido.ShouldNotBeNull();
        usuarioPersistido.PasswordHash.ShouldBe("hash-derivado");
        usuarioPersistido.PasswordHash.ShouldNotBe("senha-em-texto-claro");
        _passwordHasher.Received(1).Hash("senha-em-texto-claro");
    }

    [Fact]
    public async Task Deve_recusar_o_cadastro_quando_o_email_ja_existe()
    {
        _userRepository.EmailExistsAsync("bruno@teste.dev", Arg.Any<CancellationToken>()).Returns(true);

        await Should.ThrowAsync<ConflictException>(() => CreateHandler().Handle(
            new RegisterUserCommand("Bruno", "bruno@teste.dev", "senha-segura-123"),
            CancellationToken.None));

        // Nada pode ter sido gravado: uma tentativa recusada não deve deixar
        // rastro no banco nem publicar evento.
        await _userRepository.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await _userRepository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deve_verificar_a_unicidade_usando_o_email_normalizado()
    {
        ArrangeHappyPath();

        await CreateHandler().Handle(
            new RegisterUserCommand("Bruno", "  BRUNO@Teste.DEV  ", "senha-segura-123"),
            CancellationToken.None);

        // Consultar com o e-mail cru deixaria passar duplicatas que o índice
        // único do banco depois rejeitaria — devolvendo um 500 em vez de um 409.
        await _userRepository.Received(1).EmailExistsAsync("bruno@teste.dev", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deve_enfileirar_o_evento_de_criacao_na_outbox()
    {
        ArrangeHappyPath();
        IIntegrationEvent? eventoEnfileirado = null;
        _outboxWriter.When(writer => writer.Add(Arg.Any<IIntegrationEvent>()))
            .Do(call => eventoEnfileirado = call.Arg<IIntegrationEvent>());

        await CreateHandler().Handle(
            new RegisterUserCommand("Bruno", "bruno@teste.dev", "senha-segura-123"),
            CancellationToken.None);

        eventoEnfileirado.ShouldBeOfType<UserCreatedEvent>();
        ((UserCreatedEvent)eventoEnfileirado).Email.ShouldBe("bruno@teste.dev");
    }

    [Fact]
    public async Task Deve_salvar_usuario_e_outbox_numa_unica_transacao()
    {
        ArrangeHappyPath();

        await CreateHandler().Handle(
            new RegisterUserCommand("Bruno", "bruno@teste.dev", "senha-segura-123"),
            CancellationToken.None);

        // UM ÚNICO SaveChanges = UMA ÚNICA TRANSAÇÃO.
        //
        // É o coração do padrão Outbox: o usuário e o evento são commitados
        // juntos. Dois SaveChanges abririam a janela em que o usuário existe mas
        // o evento se perdeu — a inconsistência que o padrão existe para evitar.
        await _userRepository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deve_usar_o_mesmo_instante_para_o_usuario_e_para_o_token()
    {
        ArrangeHappyPath();
        User? usuarioPersistido = null;
        await _userRepository.AddAsync(
            Arg.Do<User>(user => usuarioPersistido = user),
            Arg.Any<CancellationToken>());

        await CreateHandler().Handle(
            new RegisterUserCommand("Bruno", "bruno@teste.dev", "senha-segura-123"),
            CancellationToken.None);

        usuarioPersistido.ShouldNotBeNull();
        usuarioPersistido.CreatedAtUtc.ShouldBe(FixedClock.DefaultInstant);
        _tokenService.Received(1).CreateTokenPair(Arg.Any<User>(), FixedClock.DefaultInstant);
    }
}
