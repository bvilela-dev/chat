using IdentityService.Application.Contracts;
using IdentityService.Domain;

namespace IdentityService.Application.Abstractions;

/// <summary>
/// Emissão do par access token + refresh token.
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Emite um novo par de tokens para o usuário.
    /// </summary>
    /// <param name="user">Usuário autenticado.</param>
    /// <param name="utcNow">
    /// Instante de referência, injetado em vez de lido internamente. É isso que
    /// permite ao teste afirmar "o token expira exatamente 15 minutos depois"
    /// sem depender do relógio real.
    /// </param>
    TokenPair CreateTokenPair(User user, DateTime utcNow);
}
