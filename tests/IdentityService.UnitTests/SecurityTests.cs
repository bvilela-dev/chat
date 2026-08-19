using System.Diagnostics;
using BuildingBlocks.AspNetCore;
using IdentityService.Domain;
using IdentityService.Infrastructure.Security;
using IdentityService.UnitTests.TestDoubles;
using Microsoft.IdentityModel.JsonWebTokens;

namespace IdentityService.UnitTests;

/// <summary>
/// Testes das primitivas de segurança: hash de senha e emissão de tokens.
/// </summary>
/// <remarks>
/// Estes testes usam as implementações REAIS (BCrypt e JWT de verdade), não
/// dublês. A distinção importa: aqui o objetivo é validar propriedades
/// criptográficas — e um mock de BCrypt validaria apenas o próprio mock.
/// </remarks>
public sealed class BcryptPasswordHasherTests
{
    private readonly BcryptPasswordHasher _hasher = new();

    [Fact]
    public void Deve_gerar_hashes_diferentes_para_a_mesma_senha()
    {
        var primeiro = _hasher.Hash("mesma-senha");
        var segundo = _hasher.Hash("mesma-senha");

        // O salt aleatório por senha é o que impede rainbow tables e faz com que
        // dois usuários com a mesma senha tenham hashes distintos — um atacante
        // que quebre um não ganha o outro de graça.
        primeiro.ShouldNotBe(segundo);
    }

    [Fact]
    public void Deve_verificar_corretamente_a_senha_original()
    {
        var hash = _hasher.Hash("senha-secreta");

        _hasher.Verify("senha-secreta", hash).ShouldBeTrue();
        _hasher.Verify("senha-errada", hash).ShouldBeFalse();
    }

    [Fact]
    public void Nao_deve_conter_a_senha_em_texto_claro_no_hash()
    {
        var hash = _hasher.Hash("senha-muito-secreta");

        hash.ShouldNotContain("senha-muito-secreta");
    }

    [Fact]
    public void Deve_retornar_false_para_um_hash_malformado()
    {
        // Um registro migrado de um sistema legado com outro algoritmo não pode
        // derrubar o login com 500 — e um 500 aqui também revelaria, pela
        // diferença de resposta, que aquele usuário existe.
        _hasher.Verify("qualquer-senha", "isto-nao-e-um-hash-bcrypt").ShouldBeFalse();
    }

    [Fact]
    public void VerifyAgainstDummyHash_deve_sempre_retornar_false()
    {
        _hasher.VerifyAgainstDummyHash("qualquer-coisa").ShouldBeFalse();
    }

    [Fact]
    public void VerifyAgainstDummyHash_deve_custar_tempo_comparavel_a_uma_verificacao_real()
    {
        // Confirma a mitigação de timing attack: o caminho do "usuário
        // inexistente" precisa gastar aproximadamente o mesmo tempo de CPU do
        // caminho normal.
        //
        // A tolerância é larga de propósito. Medir tempo em teste é
        // intrinsecamente ruidoso (agendamento do SO, JIT, CI compartilhado); o
        // que se afirma aqui é a ORDEM DE GRANDEZA. Um retorno antecipado, que é
        // a regressão temida, seria centenas de vezes mais rápido e cairia bem
        // fora desta faixa.
        var hashReal = _hasher.Hash("senha-de-referencia");

        // Descarta a primeira execução: o JIT compila o caminho na primeira
        // chamada e distorceria a medição.
        _hasher.Verify("senha-de-referencia", hashReal);
        _hasher.VerifyAgainstDummyHash("senha-de-referencia");

        var relogioReal = Stopwatch.StartNew();
        _hasher.Verify("senha-de-referencia", hashReal);
        relogioReal.Stop();

        var relogioDummy = Stopwatch.StartNew();
        _hasher.VerifyAgainstDummyHash("senha-de-referencia");
        relogioDummy.Stop();

        var proporcao = (double)relogioDummy.ElapsedTicks / Math.Max(relogioReal.ElapsedTicks, 1);
        proporcao.ShouldBeInRange(0.2, 5.0);
    }
}

/// <summary>Testes da emissão de tokens JWT e de refresh tokens.</summary>
public sealed class JwtTokenServiceTests
{
    private static readonly JwtOptions Options = new()
    {
        Issuer = "chat-identity",
        Audience = "chat-clients",
        Key = "chave-de-teste-com-mais-de-32-caracteres-para-hmac",
        AccessTokenMinutes = 15,
        RefreshTokenDays = 7
    };

    private readonly JwtTokenService _service = new(Options);

    [Fact]
    public void Deve_calcular_as_expiracoes_a_partir_do_instante_informado()
    {
        var par = _service.CreateTokenPair(CreateUser(), FixedClock.DefaultInstant);

        // Determinístico graças ao instante injetado: dá para afirmar a
        // expiração exata sem depender do relógio da máquina.
        par.AccessTokenExpiresAtUtc.ShouldBe(FixedClock.DefaultInstant.AddMinutes(15));
        par.RefreshTokenExpiresAtUtc.ShouldBe(FixedClock.DefaultInstant.AddDays(7));
    }

    [Fact]
    public void Deve_incluir_o_identificador_do_usuario_no_claim_sub()
    {
        var user = CreateUser();

        var par = _service.CreateTokenPair(user, FixedClock.DefaultInstant);
        var token = new JsonWebTokenHandler().ReadJsonWebToken(par.AccessToken);

        // É o claim que toda a autorização da plataforma consome. Se ele sumisse,
        // nenhuma verificação de participação em conversa funcionaria.
        token.GetClaim(JwtRegisteredClaimNames.Sub).Value.ShouldBe(user.Id.ToString());
    }

    [Fact]
    public void Deve_emitir_o_token_com_o_emissor_e_publico_configurados()
    {
        var par = _service.CreateTokenPair(CreateUser(), FixedClock.DefaultInstant);
        var token = new JsonWebTokenHandler().ReadJsonWebToken(par.AccessToken);

        token.Issuer.ShouldBe("chat-identity");
        token.Audiences.ShouldContain("chat-clients");
    }

    [Fact]
    public void Deve_incluir_um_jti_unico_em_cada_token()
    {
        var handler = new JsonWebTokenHandler();
        var user = CreateUser();

        var primeiro = handler.ReadJsonWebToken(_service.CreateTokenPair(user, FixedClock.DefaultInstant).AccessToken);
        var segundo = handler.ReadJsonWebToken(_service.CreateTokenPair(user, FixedClock.DefaultInstant).AccessToken);

        primeiro.GetClaim(JwtRegisteredClaimNames.Jti).Value
            .ShouldNotBe(segundo.GetClaim(JwtRegisteredClaimNames.Jti).Value);
    }

    [Fact]
    public void Nao_deve_incluir_o_hash_da_senha_no_token()
    {
        var user = CreateUser();

        var par = _service.CreateTokenPair(user, FixedClock.DefaultInstant);

        // Um JWT é apenas Base64: qualquer pessoa lê o conteúdo sem a chave. Ele
        // é à prova de ADULTERAÇÃO, não de LEITURA — nunca deve carregar segredo.
        par.AccessToken.ShouldNotContain(user.PasswordHash);
    }

    [Fact]
    public void Deve_gerar_refresh_tokens_distintos_e_com_entropia_suficiente()
    {
        var user = CreateUser();

        var tokens = Enumerable.Range(0, 100)
            .Select(_ => _service.CreateTokenPair(user, FixedClock.DefaultInstant).RefreshToken)
            .ToArray();

        // Sem colisões: o refresh token é a chave de busca no banco e precisa ser
        // único.
        tokens.Distinct().Count().ShouldBe(100);

        // 32 bytes em Base64 URL-safe resultam em 43 caracteres. Comprimento
        // menor indicaria queda de entropia — foi exatamente o problema da versão
        // anterior, que derivava o token de GUIDs (projetados para unicidade, não
        // para imprevisibilidade).
        tokens[0].Length.ShouldBeGreaterThanOrEqualTo(43);
    }

    [Fact]
    public void O_refresh_token_deve_ser_seguro_para_uso_em_url()
    {
        var par = _service.CreateTokenPair(CreateUser(), FixedClock.DefaultInstant);

        // Base64 URL-safe: sem '+', '/' ou '=' que precisariam de escape ao
        // trafegar em URL, cabeçalho ou corpo de formulário.
        par.RefreshToken.ShouldNotContain("+");
        par.RefreshToken.ShouldNotContain("/");
        par.RefreshToken.ShouldNotContain("=");
    }

    private static User CreateUser()
    {
        return User.Register("Bruno", "bruno@teste.dev", "hash-armazenado", FixedClock.DefaultInstant);
    }
}
