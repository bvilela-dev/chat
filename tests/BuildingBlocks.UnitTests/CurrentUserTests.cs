using System.Security.Claims;
using BuildingBlocks.Application;
using BuildingBlocks.AspNetCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Shouldly;

namespace BuildingBlocks.UnitTests;

/// <summary>
/// Testes da extração de identidade a partir do <see cref="ClaimsPrincipal"/>.
/// </summary>
/// <remarks>
/// Parece utilitário trivial, mas é o alicerce de toda a autorização da
/// plataforma: se <c>GetRequiredUserId</c> devolvesse o usuário errado, TODAS as
/// verificações de participação em conversa passariam a proteger a pessoa
/// errada. Merece cobertura direta.
/// </remarks>
public sealed class CurrentUserTests
{
    private static ClaimsPrincipal BuildPrincipal(params Claim[] claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"));
    }

    [Fact]
    public void Deve_ler_o_identificador_a_partir_do_claim_sub()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()));

        principal.GetRequiredUserId().ShouldBe(userId);
    }

    [Fact]
    public void Deve_ler_o_identificador_a_partir_do_claim_NameIdentifier()
    {
        // O .NET remapeia `sub` para ClaimTypes.NameIdentifier em algumas
        // configurações. Suportar as duas grafias deixa o código imune a essa
        // diferença de host — a origem de uma classe inteira de bugs em que a
        // autenticação "funciona em dev e falha em produção".
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(new Claim(ClaimTypes.NameIdentifier, userId.ToString()));

        principal.GetRequiredUserId().ShouldBe(userId);
    }

    [Fact]
    public void Deve_lançar_UnauthorizedException_quando_nao_ha_identificador()
    {
        var principal = BuildPrincipal(new Claim(ClaimTypes.Email, "bruno@teste.dev"));

        // Falhar explicitamente é essencial. Devolver Guid.Empty faria as
        // consultas seguintes rodarem "em nome de um usuário zerado", sem erro
        // algum — e o problema só apareceria como dados faltando.
        Should.Throw<UnauthorizedException>(() => principal.GetRequiredUserId());
    }

    [Fact]
    public void Deve_lançar_UnauthorizedException_quando_o_identificador_nao_e_um_Guid()
    {
        var principal = BuildPrincipal(new Claim(JwtRegisteredClaimNames.Sub, "nao-e-um-guid"));

        Should.Throw<UnauthorizedException>(() => principal.GetRequiredUserId());
    }

    [Fact]
    public void EnsureIsSelf_deve_permitir_quando_o_usuario_e_o_proprio()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()));

        Should.NotThrow(() => principal.EnsureIsSelf(userId));
    }

    [Fact]
    public void EnsureIsSelf_deve_bloquear_o_acesso_a_recurso_de_outro_usuario()
    {
        // Este é o teste que trava a defesa contra IDOR: um usuário autenticado
        // pedindo o recurso de outro precisa receber 403.
        var principal = BuildPrincipal(new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()));

        Should.Throw<ForbiddenException>(() => principal.EnsureIsSelf(Guid.NewGuid()));
    }

    [Fact]
    public void GetDisplayName_deve_cair_para_o_identificador_quando_nao_ha_nome()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()));

        // Degradar em vez de falhar: um claim de nome ausente não deve impedir o
        // envio de uma mensagem.
        principal.GetDisplayName().ShouldBe(userId.ToString());
    }

    [Fact]
    public void TryGetUserId_deve_retornar_false_para_principal_nulo()
    {
        ClaimsPrincipal? principal = null;

        principal.TryGetUserId(out var userId).ShouldBeFalse();
        userId.ShouldBe(Guid.Empty);
    }
}
