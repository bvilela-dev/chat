using BuildingBlocks.AspNetCore;
using Shouldly;

namespace BuildingBlocks.UnitTests;

/// <summary>
/// Testes das opções de JWT, com foco na detecção da chave insegura.
/// </summary>
public sealed class JwtOptionsTests
{
    [Fact]
    public void Deve_identificar_a_chave_de_desenvolvimento_como_insegura()
    {
        var options = new JwtOptions { Key = JwtOptions.InsecureDevelopmentKey };

        // É esta propriedade que faz o startup abortar em produção. Sem ela, um
        // secret não montado passaria despercebido e a plataforma assinaria
        // tokens com uma chave pública, versionada no Git.
        options.UsesInsecureDevelopmentKey.ShouldBeTrue();
    }

    [Fact]
    public void Deve_aceitar_uma_chave_personalizada_como_segura()
    {
        var options = new JwtOptions { Key = "uma-chave-bem-longa-e-exclusiva-deste-ambiente-2026" };

        options.UsesInsecureDevelopmentKey.ShouldBeFalse();
    }

    [Fact]
    public void Deve_usar_padroes_conservadores_de_expiracao()
    {
        var options = new JwtOptions();

        // Access token curto porque JWT não é revogável; a continuidade da sessão
        // fica por conta do refresh token, esse sim revogável.
        options.AccessTokenMinutes.ShouldBe(15);
        options.RefreshTokenDays.ShouldBe(7);
    }
}
