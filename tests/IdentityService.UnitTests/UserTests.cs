using IdentityService.Domain;
using IdentityService.UnitTests.TestDoubles;

namespace IdentityService.UnitTests;

/// <summary>
/// Testes da entidade de domínio <see cref="User"/>.
/// </summary>
/// <remarks>
/// Não há mock nenhum aqui: a entidade não depende de banco, relógio ou rede.
/// Esse é justamente o benefício de um domínio isolado — a lógica de negócio é
/// exercitada em microssegundos, sem nenhuma infraestrutura.
/// </remarks>
public sealed class UserTests
{
    [Theory]
    [InlineData("Bruno@Teste.DEV", "bruno@teste.dev")]
    [InlineData("  bruno@teste.dev  ", "bruno@teste.dev")]
    [InlineData("BRUNO@TESTE.DEV", "bruno@teste.dev")]
    public void Deve_normalizar_o_email_no_cadastro(string entrada, string esperado)
    {
        // A normalização é o que garante que "Ana@X.com" e "ana@x.com" sejam
        // tratados como a mesma conta — tanto na checagem de unicidade quanto no
        // login. Sem isso, seria possível cadastrar o "mesmo" e-mail duas vezes.
        var user = User.Register("Bruno", entrada, "hash", FixedClock.DefaultInstant);

        user.Email.ShouldBe(esperado);
    }

    [Fact]
    public void Deve_remover_espacos_das_pontas_do_nome()
    {
        var user = User.Register("  Bruno Vilela  ", "bruno@teste.dev", "hash", FixedClock.DefaultInstant);

        user.Name.ShouldBe("Bruno Vilela");
    }

    [Fact]
    public void Deve_gerar_identificadores_distintos_para_usuarios_distintos()
    {
        var primeiro = User.Register("A", "a@teste.dev", "hash", FixedClock.DefaultInstant);
        var segundo = User.Register("B", "b@teste.dev", "hash", FixedClock.DefaultInstant);

        primeiro.Id.ShouldNotBe(segundo.Id);
        primeiro.Id.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void Deve_iniciar_sem_nenhum_refresh_token()
    {
        var user = User.Register("Bruno", "bruno@teste.dev", "hash", FixedClock.DefaultInstant);

        user.RefreshTokens.ShouldBeEmpty();
    }

    [Fact]
    public void Deve_encontrar_um_refresh_token_ativo()
    {
        var user = User.Register("Bruno", "bruno@teste.dev", "hash", FixedClock.DefaultInstant);
        var expiraEm = FixedClock.DefaultInstant.AddDays(7);

        user.IssueRefreshToken("token-abc", expiraEm, FixedClock.DefaultInstant);

        var encontrado = user.GetActiveRefreshToken("token-abc", FixedClock.DefaultInstant);

        encontrado.ShouldNotBeNull();
        encontrado.Token.ShouldBe("token-abc");
    }

    [Fact]
    public void Nao_deve_encontrar_um_refresh_token_expirado()
    {
        var user = User.Register("Bruno", "bruno@teste.dev", "hash", FixedClock.DefaultInstant);
        var expiraEm = FixedClock.DefaultInstant.AddDays(7);

        user.IssueRefreshToken("token-abc", expiraEm, FixedClock.DefaultInstant);

        // Aqui está o valor do relógio injetado: "sete dias e um segundo depois"
        // é apenas uma aritmética, não uma espera.
        var depoisDaExpiracao = expiraEm.AddSeconds(1);

        user.GetActiveRefreshToken("token-abc", depoisDaExpiracao).ShouldBeNull();
    }

    [Fact]
    public void Nao_deve_encontrar_um_refresh_token_revogado()
    {
        var user = User.Register("Bruno", "bruno@teste.dev", "hash", FixedClock.DefaultInstant);
        var token = user.IssueRefreshToken("token-abc", FixedClock.DefaultInstant.AddDays(7), FixedClock.DefaultInstant);

        token.Revoke(FixedClock.DefaultInstant);

        // É o que sustenta a rotação: uma vez usado, o token não vale mais,
        // mesmo estando dentro do prazo de validade.
        user.GetActiveRefreshToken("token-abc", FixedClock.DefaultInstant).ShouldBeNull();
    }

    [Fact]
    public void Nao_deve_encontrar_um_token_inexistente()
    {
        var user = User.Register("Bruno", "bruno@teste.dev", "hash", FixedClock.DefaultInstant);
        user.IssueRefreshToken("token-abc", FixedClock.DefaultInstant.AddDays(7), FixedClock.DefaultInstant);

        user.GetActiveRefreshToken("token-que-nao-existe", FixedClock.DefaultInstant).ShouldBeNull();
    }

    [Fact]
    public void A_colecao_de_refresh_tokens_deve_ser_somente_leitura()
    {
        var user = User.Register("Bruno", "bruno@teste.dev", "hash", FixedClock.DefaultInstant);

        // Se a coleção fosse exposta como List<T>, código externo poderia
        // adicionar tokens driblando IssueRefreshToken — e o encapsulamento da
        // entidade seria apenas decorativo.
        user.RefreshTokens.ShouldBeAssignableTo<IReadOnlyCollection<RefreshToken>>();
        (user.RefreshTokens is List<RefreshToken>).ShouldBeFalse();
    }
}
