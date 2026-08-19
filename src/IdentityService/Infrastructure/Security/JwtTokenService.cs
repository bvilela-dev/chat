using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.AspNetCore;
using IdentityService.Application.Abstractions;
using IdentityService.Application.Contracts;
using IdentityService.Domain;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace IdentityService.Infrastructure.Security;

/// <summary>
/// Emite o access token (JWT assinado) e o refresh token (valor opaco aleatório).
/// </summary>
public sealed class JwtTokenService(JwtOptions options) : ITokenService
{
    /// <summary>
    /// Tamanho em bytes do refresh token.
    /// </summary>
    /// <remarks>
    /// 32 bytes = 256 bits de entropia. É o mesmo nível de uma chave AES-256 e
    /// torna a adivinhação por força bruta inviável dentro de qualquer horizonte
    /// prático.
    /// </remarks>
    private const int RefreshTokenBytes = 32;

    /// <inheritdoc />
    public TokenPair CreateTokenPair(User user, DateTime utcNow)
    {
        var accessTokenExpiresAtUtc = utcNow.AddMinutes(options.AccessTokenMinutes);
        var refreshTokenExpiresAtUtc = utcNow.AddDays(options.RefreshTokenDays);

        return new TokenPair(
            CreateAccessToken(user, utcNow, accessTokenExpiresAtUtc),
            accessTokenExpiresAtUtc,
            CreateRefreshToken(),
            refreshTokenExpiresAtUtc);
    }

    private string CreateAccessToken(User user, DateTime issuedAtUtc, DateTime expiresAtUtc)
    {
        var claims = new List<Claim>
        {
            // `sub` é o claim padrão (RFC 7519) para o identificador do usuário.
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),

            // `jti` é um identificador único do token. Não é usado hoje, mas é a
            // peça necessária caso seja preciso implementar uma denylist de
            // access tokens revogados antes do vencimento.
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),

            new(JwtRegisteredClaimNames.Email, user.Email),

            // O nome vai no token para que o Chat Service possa carimbar o autor
            // da mensagem sem consultar o Identity Service a cada envio. É uma
            // desnormalização deliberada: troca uma chamada de rede por dado
            // ligeiramente defasado — se o usuário mudar o nome, as mensagens já
            // enviadas mantêm o nome antigo, o que é o comportamento esperado.
            new(ClaimTypes.Name, user.Name)
        };

        var signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Key)),
            SecurityAlgorithms.HmacSha256);

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            IssuedAt = issuedAtUtc,
            NotBefore = issuedAtUtc,
            Expires = expiresAtUtc,
            Issuer = options.Issuer,
            Audience = options.Audience,
            SigningCredentials = signingCredentials
        };

        // JsonWebTokenHandler é o sucessor do JwtSecurityTokenHandler: mais
        // rápido, com menos alocações e sem o remapeamento implícito de claims
        // que costuma surpreender quem lê o token do outro lado.
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    /// <summary>
    /// Gera um refresh token opaco e criptograficamente aleatório.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Correção importante em relação à versão anterior.</b> O código original
    /// construía o token concatenando dois GUIDs:
    /// </para>
    /// <code>
    /// Convert.ToBase64String(Guid.NewGuid().ToByteArray()) +
    /// Convert.ToBase64String(Guid.NewGuid().ToByteArray())
    /// </code>
    /// <para>
    /// Parece aleatório, mas não é adequado para uso como segredo.
    /// <c>Guid.NewGuid()</c> gera um UUID v4, cujos 128 bits incluem 6 bits fixos
    /// de versão e variante — e, mais grave, a especificação não exige que seja
    /// gerado por um PRNG <i>criptográfico</i>. GUID foi projetado para garantir
    /// <b>unicidade</b>, não <b>imprevisibilidade</b>: são propriedades
    /// diferentes, e usar um no lugar do outro é um erro clássico.
    /// </para>
    /// <para>
    /// <see cref="RandomNumberGenerator"/> usa o CSPRNG do sistema operacional,
    /// que é a fonte correta para material secreto. Base64 <i>URL-safe</i> evita
    /// os caracteres <c>+</c>, <c>/</c> e <c>=</c>, que exigiriam escape ao
    /// trafegar em URL ou cabeçalho.
    /// </para>
    /// </remarks>
    private static string CreateRefreshToken()
    {
        var buffer = RandomNumberGenerator.GetBytes(RefreshTokenBytes);
        return Base64UrlEncoder.Encode(buffer);
    }
}
