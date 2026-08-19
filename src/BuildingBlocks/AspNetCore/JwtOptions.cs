using System.ComponentModel.DataAnnotations;

namespace BuildingBlocks.AspNetCore;

/// <summary>
/// Parâmetros de emissão e validação dos tokens JWT, lidos da seção
/// <c>"Jwt"</c> da configuração.
/// </summary>
/// <remarks>
/// <para>
/// Todos os serviços compartilham a mesma chave simétrica (HMAC-SHA256): o
/// Identity Service <i>assina</i> e os demais apenas <i>verificam</i>. É a opção
/// adequada para um sistema deste porte, sob um único domínio de confiança.
/// </para>
/// <para>
/// <b>Como isso evoluiria em produção real:</b> migrar para chave assimétrica
/// (RS256/ES256). Aí somente o Identity Service guarda a chave privada e os
/// outros serviços validam com a chave pública, distribuída via JWKS. A vantagem
/// é que o vazamento de um serviço de leitura deixa de permitir a <b>forja</b>
/// de tokens — com HMAC, quem consegue verificar também consegue assinar.
/// </para>
/// </remarks>
public sealed class JwtOptions
{
    /// <summary>Nome da seção de configuração correspondente.</summary>
    public const string SectionName = "Jwt";

    /// <summary>
    /// Valor de placeholder que acompanha o repositório. Existe para que o
    /// projeto rode com <c>dotnet run</c> sem configuração adicional, e é
    /// explicitamente <b>bloqueado</b> fora do ambiente de desenvolvimento.
    /// </summary>
    public const string InsecureDevelopmentKey = "super-secret-development-key-change-me";

    /// <summary>Emissor esperado do token (claim <c>iss</c>).</summary>
    [Required]
    public string Issuer { get; init; } = "chat-identity";

    /// <summary>Público-alvo esperado do token (claim <c>aud</c>).</summary>
    [Required]
    public string Audience { get; init; } = "chat-clients";

    /// <summary>
    /// Segredo usado para assinar e verificar o token.
    /// </summary>
    /// <remarks>
    /// O mínimo de 32 caracteres não é arbitrário: o HMAC-SHA256 opera com blocos
    /// de 256 bits, e uma chave mais curta é internamente preenchida com zeros,
    /// reduzindo a entropia real da assinatura.
    /// </remarks>
    [Required]
    [MinLength(32, ErrorMessage = "A chave JWT precisa de ao menos 32 caracteres para uso seguro com HMAC-SHA256.")]
    public string Key { get; init; } = InsecureDevelopmentKey;

    /// <summary>
    /// Validade do access token, em minutos.
    /// </summary>
    /// <remarks>
    /// Curta de propósito. Um JWT não pode ser revogado — uma vez emitido, vale
    /// até expirar. A janela curta limita o estrago de um token vazado; a
    /// continuidade da sessão fica por conta do refresh token, esse sim
    /// revogável porque é consultado no banco a cada uso.
    /// </remarks>
    [Range(1, 1440)]
    public int AccessTokenMinutes { get; init; } = 15;

    /// <summary>Validade do refresh token, em dias.</summary>
    [Range(1, 365)]
    public int RefreshTokenDays { get; init; } = 7;

    /// <summary>
    /// Indica se a configuração ainda está usando a chave de exemplo do repositório.
    /// </summary>
    public bool UsesInsecureDevelopmentKey =>
        string.Equals(Key, InsecureDevelopmentKey, StringComparison.Ordinal);
}
