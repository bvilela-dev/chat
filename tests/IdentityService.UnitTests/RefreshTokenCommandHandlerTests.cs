using BuildingBlocks.Application;
using IdentityService.Application.Abstractions;
using IdentityService.Application.Contracts;
using IdentityService.Application.Users;
using IdentityService.Domain;
using IdentityService.UnitTests.TestDoubles;

namespace IdentityService.UnitTests;

/// <summary>
/// Testes da renovação de sessão, com foco na rotação de refresh token.
/// </summary>
public sealed class RefreshTokenCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();
    private readonly FixedClock _clock = FixedClock.Default();

    private RefreshTokenCommandHandler CreateHandler()
    {
        return new RefreshTokenCommandHandler(_userRepository, _tokenService, _clock);
    }

    private static User CreateUserWithToken(string token, DateTime expiresAtUtc)
    {
        var user = User.Register("Bruno", "bruno@teste.dev", "hash", FixedClock.DefaultInstant);
        user.IssueRefreshToken(token, expiresAtUtc, FixedClock.DefaultInstant);
        return user;
    }

    private void ArrangeTokenService()
    {
        _tokenService.CreateTokenPair(Arg.Any<User>(), Arg.Any<DateTime>()).Returns(
            new TokenPair(
                "novo-access-token",
                FixedClock.DefaultInstant.AddMinutes(15),
                "novo-refresh-token",
                FixedClock.DefaultInstant.AddDays(7)));
    }

    [Fact]
    public async Task Deve_renovar_a_sessao_com_um_refresh_token_valido()
    {
        var user = CreateUserWithToken("token-antigo", FixedClock.DefaultInstant.AddDays(7));
        _userRepository.GetByRefreshTokenAsync("token-antigo", Arg.Any<CancellationToken>()).Returns(user);
        ArrangeTokenService();

        var resposta = await CreateHandler().Handle(
            new RefreshTokenCommand("token-antigo"),
            CancellationToken.None);

        resposta.AccessToken.ShouldBe("novo-access-token");
        resposta.RefreshToken.ShouldBe("novo-refresh-token");
    }

    [Fact]
    public async Task Deve_revogar_o_token_apresentado_ao_renovar()
    {
        // ROTAÇÃO DE REFRESH TOKEN — a propriedade de segurança central deste caso de uso.
        //
        // Cada refresh token vale por um único uso. Sem isso, um token vazado
        // daria ao atacante sete dias de acesso silencioso; com rotação, o
        // primeiro uso pelo atacante derruba a sessão da vítima, tornando o
        // incidente visível.
        var user = CreateUserWithToken("token-antigo", FixedClock.DefaultInstant.AddDays(7));
        _userRepository.GetByRefreshTokenAsync("token-antigo", Arg.Any<CancellationToken>()).Returns(user);
        ArrangeTokenService();

        await CreateHandler().Handle(new RefreshTokenCommand("token-antigo"), CancellationToken.None);

        var tokenAntigo = user.RefreshTokens.Single(token => token.Token == "token-antigo");
        tokenAntigo.IsRevoked.ShouldBeTrue();
        tokenAntigo.RevokedAtUtc.ShouldBe(FixedClock.DefaultInstant);
    }

    [Fact]
    public async Task Nao_deve_aceitar_o_mesmo_refresh_token_duas_vezes()
    {
        var user = CreateUserWithToken("token-antigo", FixedClock.DefaultInstant.AddDays(7));
        _userRepository.GetByRefreshTokenAsync("token-antigo", Arg.Any<CancellationToken>()).Returns(user);
        ArrangeTokenService();

        await CreateHandler().Handle(new RefreshTokenCommand("token-antigo"), CancellationToken.None);

        // A segunda tentativa com o mesmo token precisa falhar — é o que
        // caracteriza o uso único.
        await Should.ThrowAsync<UnauthorizedException>(() => CreateHandler().Handle(
            new RefreshTokenCommand("token-antigo"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Deve_recusar_um_refresh_token_desconhecido()
    {
        _userRepository.GetByRefreshTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        await Should.ThrowAsync<UnauthorizedException>(() => CreateHandler().Handle(
            new RefreshTokenCommand("token-inventado"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Deve_recusar_um_refresh_token_expirado()
    {
        var expiraEm = FixedClock.DefaultInstant.AddDays(7);
        var user = CreateUserWithToken("token-antigo", expiraEm);
        _userRepository.GetByRefreshTokenAsync("token-antigo", Arg.Any<CancellationToken>()).Returns(user);

        // Avança o relógio para além da expiração — sem esperar sete dias.
        _clock.Advance(TimeSpan.FromDays(8));

        await Should.ThrowAsync<UnauthorizedException>(() => CreateHandler().Handle(
            new RefreshTokenCommand("token-antigo"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Deve_registrar_explicitamente_o_novo_refresh_token_para_inclusao()
    {
        // TESTE DE REGRESSÃO DE UM BUG DE PERSISTÊNCIA REAL.
        //
        // A versão anterior confiava na detecção de mudanças do EF Core para
        // perceber o token novo dentro da coleção do agregado. Como as chaves Guid
        // são atribuídas pelo domínio e o EF as assume geradas pelo banco, ele
        // classificava a entidade como `Modified` e emitia um UPDATE numa linha
        // inexistente — falhando com DbUpdateConcurrencyException.
        //
        // O efeito era que a ROTAÇÃO INTEIRA falhava: nem o token novo era
        // inserido, nem o antigo revogado, e o usuário recebia 500.
        //
        // A causa raiz foi corrigida no mapeamento (`ValueGeneratedNever`); este
        // teste trava a segunda linha de defesa, que é declarar a intenção de
        // persistência de forma explícita.
        var user = CreateUserWithToken("token-antigo", FixedClock.DefaultInstant.AddDays(7));
        _userRepository.GetByRefreshTokenAsync("token-antigo", Arg.Any<CancellationToken>()).Returns(user);
        ArrangeTokenService();

        await CreateHandler().Handle(new RefreshTokenCommand("token-antigo"), CancellationToken.None);

        _userRepository.Received(1).AddRefreshToken(
            Arg.Is<RefreshToken>(token => token.Token == "novo-refresh-token"));
    }

    [Fact]
    public async Task Deve_emitir_o_novo_token_e_revogar_o_antigo_numa_unica_transacao()
    {
        var user = CreateUserWithToken("token-antigo", FixedClock.DefaultInstant.AddDays(7));
        _userRepository.GetByRefreshTokenAsync("token-antigo", Arg.Any<CancellationToken>()).Returns(user);
        ArrangeTokenService();

        await CreateHandler().Handle(new RefreshTokenCommand("token-antigo"), CancellationToken.None);

        // Atomicidade: não pode existir um instante em que o token antigo já foi
        // revogado e o novo ainda não foi gravado — o usuário ficaria sem sessão.
        await _userRepository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        user.RefreshTokens.Count.ShouldBe(2);
        user.GetActiveRefreshToken("novo-refresh-token", FixedClock.DefaultInstant).ShouldNotBeNull();
    }
}
