using BuildingBlocks.AspNetCore;
using IdentityService.Application.Contracts;
using IdentityService.Application.Users;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IdentityService.API.Controllers;

/// <summary>
/// Endpoints públicos de cadastro, login e renovação de sessão.
/// </summary>
/// <remarks>
/// <para>
/// Único controller da plataforma sem <c>[Authorize]</c> — por definição, quem
/// chama estes endpoints ainda não tem token.
/// </para>
/// <para>
/// Justamente por serem anônimos, todos estão sob a política de rate limiting.
/// Ver <see cref="RateLimitingExtensions"/> para o raciocínio completo.
/// </para>
/// </remarks>
[ApiController]
[Route("api/auth")]
[EnableRateLimiting(RateLimitingExtensions.AuthenticationPolicy)]
[Produces("application/json")]
public sealed class AuthController(ISender sender) : ControllerBase
{
    /// <summary>Cria uma conta e já devolve uma sessão autenticada.</summary>
    /// <response code="200">Conta criada; access token e refresh token emitidos.</response>
    /// <response code="400">Dados inválidos (e-mail malformado, senha curta demais).</response>
    /// <response code="409">Já existe conta com este e-mail.</response>
    /// <response code="429">Tentativas em excesso.</response>
    [HttpPost("register")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public Task<AuthResponse> Register(
        [FromBody] RegisterUserCommand command,
        CancellationToken cancellationToken)
    {
        // O controller é deliberadamente magro: recebe, delega ao MediatR e
        // devolve. Toda a regra vive no handler, o que a torna testável sem
        // levantar um servidor HTTP.
        //
        // O comando é vinculado direto do corpo da requisição. Isso é seguro aqui
        // porque RegisterUserCommand não tem nenhum campo privilegiado — todos os
        // seus campos podem legitimamente vir do cliente. Em comandos que
        // carregam identificadores derivados do token, esse atalho seria uma
        // brecha de "mass assignment", e por isso os outros controllers montam o
        // comando explicitamente.
        return sender.Send(command, cancellationToken);
    }

    /// <summary>Autentica com e-mail e senha.</summary>
    /// <response code="200">Credenciais válidas.</response>
    /// <response code="401">E-mail ou senha inválidos.</response>
    /// <response code="429">Tentativas em excesso.</response>
    [HttpPost("login")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public Task<AuthResponse> Login(
        [FromBody] LoginUserCommand command,
        CancellationToken cancellationToken)
    {
        return sender.Send(command, cancellationToken);
    }

    /// <summary>Troca um refresh token válido por um novo par de tokens.</summary>
    /// <remarks>
    /// O refresh token apresentado é revogado no processo (rotação de uso único).
    /// </remarks>
    /// <response code="200">Sessão renovada.</response>
    /// <response code="401">Refresh token inválido, expirado ou já utilizado.</response>
    [HttpPost("refresh")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public Task<AuthResponse> Refresh(
        [FromBody] RefreshTokenCommand command,
        CancellationToken cancellationToken)
    {
        return sender.Send(command, cancellationToken);
    }
}
