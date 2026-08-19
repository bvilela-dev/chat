using BuildingBlocks.Application;
using IdentityService.Application.Abstractions;
using IdentityService.Application.Contracts;
using IdentityService.Application.Users;
using IdentityService.Domain;
using IdentityService.UnitTests.TestDoubles;

namespace IdentityService.UnitTests;

/// <summary>
/// Testes do caso de uso de login, com foco nas propriedades de segurança.
/// </summary>
public sealed class LoginUserCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();
    private readonly FixedClock _clock = FixedClock.Default();

    private LoginUserCommandHandler CreateHandler()
    {
        return new LoginUserCommandHandler(_userRepository, _passwordHasher, _tokenService, _clock);
    }

    private static User CreateUser()
    {
        return User.Register("Bruno", "bruno@teste.dev", "hash-armazenado", FixedClock.DefaultInstant);
    }

    private void ArrangeTokenService()
    {
        _tokenService.CreateTokenPair(Arg.Any<User>(), Arg.Any<DateTime>()).Returns(
            new TokenPair(
                "access-token",
                FixedClock.DefaultInstant.AddMinutes(15),
                "refresh-token",
                FixedClock.DefaultInstant.AddDays(7)));
    }

    [Fact]
    public async Task Deve_autenticar_com_credenciais_validas()
    {
        var user = CreateUser();
        _userRepository.GetByEmailAsync("bruno@teste.dev", Arg.Any<CancellationToken>()).Returns(user);
        _passwordHasher.Verify("senha-correta", "hash-armazenado").Returns(true);
        ArrangeTokenService();

        var resposta = await CreateHandler().Handle(
            new LoginUserCommand("bruno@teste.dev", "senha-correta"),
            CancellationToken.None);

        resposta.AccessToken.ShouldBe("access-token");
        resposta.User.Email.ShouldBe("bruno@teste.dev");
    }

    [Fact]
    public async Task Deve_recusar_senha_incorreta()
    {
        var user = CreateUser();
        _userRepository.GetByEmailAsync("bruno@teste.dev", Arg.Any<CancellationToken>()).Returns(user);
        _passwordHasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        await Should.ThrowAsync<UnauthorizedException>(() => CreateHandler().Handle(
            new LoginUserCommand("bruno@teste.dev", "senha-errada"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Deve_recusar_email_inexistente()
    {
        _userRepository.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);
        _passwordHasher.VerifyAgainstDummyHash(Arg.Any<string>()).Returns(false);

        await Should.ThrowAsync<UnauthorizedException>(() => CreateHandler().Handle(
            new LoginUserCommand("nao-existe@teste.dev", "qualquer-senha"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Deve_usar_a_mesma_mensagem_para_email_inexistente_e_senha_errada()
    {
        // PROPRIEDADE DE SEGURANÇA: o login não pode funcionar como oráculo de
        // enumeração de usuários. Se as mensagens diferissem, um atacante
        // submeteria uma lista de e-mails e descobriria quais estão cadastrados —
        // insumo direto para phishing dirigido e credential stuffing.

        _userRepository.GetByEmailAsync("nao-existe@teste.dev", Arg.Any<CancellationToken>()).Returns((User?)null);
        _passwordHasher.VerifyAgainstDummyHash(Arg.Any<string>()).Returns(false);

        var erroEmailInexistente = await Should.ThrowAsync<UnauthorizedException>(() => CreateHandler().Handle(
            new LoginUserCommand("nao-existe@teste.dev", "senha"),
            CancellationToken.None));

        _userRepository.GetByEmailAsync("bruno@teste.dev", Arg.Any<CancellationToken>()).Returns(CreateUser());
        _passwordHasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        var erroSenhaErrada = await Should.ThrowAsync<UnauthorizedException>(() => CreateHandler().Handle(
            new LoginUserCommand("bruno@teste.dev", "senha-errada"),
            CancellationToken.None));

        erroEmailInexistente.Message.ShouldBe(erroSenhaErrada.Message);
    }

    [Fact]
    public async Task Deve_gastar_tempo_de_hash_mesmo_quando_o_usuario_nao_existe()
    {
        // MITIGAÇÃO DE TIMING ATTACK.
        //
        // Mensagens iguais não bastam se os tempos de resposta forem diferentes:
        // ~1 ms para "usuário não existe" contra ~250 ms para "senha errada" é
        // uma diferença trivialmente mensurável pela rede, e recria o mesmo
        // oráculo de enumeração.
        //
        // Este teste garante que o caminho do usuário inexistente também paga o
        // custo do BCrypt.
        _userRepository.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);
        _passwordHasher.VerifyAgainstDummyHash(Arg.Any<string>()).Returns(false);

        await Should.ThrowAsync<UnauthorizedException>(() => CreateHandler().Handle(
            new LoginUserCommand("nao-existe@teste.dev", "qualquer-senha"),
            CancellationToken.None));

        _passwordHasher.Received(1).VerifyAgainstDummyHash("qualquer-senha");
    }

    [Fact]
    public async Task Deve_normalizar_o_email_antes_de_buscar()
    {
        _userRepository.GetByEmailAsync("bruno@teste.dev", Arg.Any<CancellationToken>()).Returns(CreateUser());
        _passwordHasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        ArrangeTokenService();

        await CreateHandler().Handle(
            new LoginUserCommand("  BRUNO@Teste.DEV  ", "senha-correta"),
            CancellationToken.None);

        // Sem normalização, o usuário que digitasse o e-mail com maiúsculas não
        // conseguiria entrar na própria conta.
        await _userRepository.Received(1).GetByEmailAsync("bruno@teste.dev", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deve_emitir_um_novo_refresh_token_a_cada_login()
    {
        var user = CreateUser();
        _userRepository.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);
        _passwordHasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        ArrangeTokenService();

        await CreateHandler().Handle(
            new LoginUserCommand("bruno@teste.dev", "senha-correta"),
            CancellationToken.None);

        // Cada sessão tem seu próprio refresh token: sair no celular não pode
        // derrubar a sessão do notebook.
        user.RefreshTokens.Count.ShouldBe(1);

        // Registro explícito para inclusão — ver a nota de regressão em
        // RefreshTokenCommandHandlerTests.
        _userRepository.Received(1).AddRefreshToken(Arg.Any<RefreshToken>());

        await _userRepository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
