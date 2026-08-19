using BuildingBlocks.Application;
using FluentValidation;
using IdentityService.Application.Abstractions;
using IdentityService.Application.Contracts;
using MediatR;

namespace IdentityService.Application.Users;

/// <summary>Comando de renovação da sessão a partir de um refresh token.</summary>
public sealed record RefreshTokenCommand(string RefreshToken) : IRequest<AuthResponse>;

/// <summary>Regras de formato da renovação.</summary>
public sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    /// <summary>Configura as regras.</summary>
    public RefreshTokenCommandValidator()
    {
        RuleFor(command => command.RefreshToken).NotEmpty();
    }
}

/// <summary>
/// Troca um refresh token válido por um novo par de tokens, aplicando rotação.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rotação de refresh token.</b> O token apresentado é revogado e um novo é
/// emitido no mesmo passo. Cada refresh token vale por um único uso.
/// </para>
/// <para>
/// Isso limita bastante o dano de um vazamento. Um refresh token de 7 dias que
/// pudesse ser reutilizado daria ao atacante uma semana inteira de acesso
/// silencioso. Com rotação, na primeira vez que o atacante o usa, o token do
/// usuário legítimo deixa de funcionar — e a sessão interrompida é um sinal
/// visível de que algo aconteceu.
/// </para>
/// <para>
/// <b>Evolução natural:</b> detecção de reuso. Guardando qual token substituiu
/// qual, a apresentação de um token já revogado indica roubo com alta confiança,
/// e a resposta correta é revogar toda a família de tokens daquele usuário,
/// derrubando as sessões dos dois lados. Ficou fora do escopo atual, mas o
/// modelo de dados (<c>RefreshToken.IsRevoked</c> + <c>RevokedAtUtc</c>) já
/// comporta a mudança.
/// </para>
/// </remarks>
public sealed class RefreshTokenCommandHandler(
    IUserRepository userRepository,
    ITokenService tokenService,
    IClock clock)
    : IRequestHandler<RefreshTokenCommand, AuthResponse>
{
    /// <inheritdoc />
    public async Task<AuthResponse> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByRefreshTokenAsync(request.RefreshToken, cancellationToken)
            ?? throw new UnauthorizedException("Refresh token inválido.");

        var utcNow = clock.UtcNow;

        // A entidade decide se o token está ativo (não revogado e não expirado).
        // A regra vive no domínio, não aqui — o handler apenas orquestra.
        var presentedToken = user.GetActiveRefreshToken(request.RefreshToken, utcNow)
            ?? throw new UnauthorizedException("Refresh token expirado ou revogado.");

        // Rotação: invalida o token apresentado antes de emitir o substituto.
        presentedToken.Revoke(utcNow);

        var tokens = tokenService.CreateTokenPair(user, utcNow);
        var newRefreshToken = user.IssueRefreshToken(tokens.RefreshToken, tokens.RefreshTokenExpiresAtUtc, utcNow);
        userRepository.AddRefreshToken(newRefreshToken);

        // A revogação do antigo e a criação do novo são commitadas juntas: não
        // existe janela em que o usuário fique sem nenhum token válido.
        await userRepository.SaveChangesAsync(cancellationToken);

        return new AuthResponse(
            tokens.AccessToken,
            tokens.AccessTokenExpiresAtUtc,
            tokens.RefreshToken,
            tokens.RefreshTokenExpiresAtUtc,
            user.ToDto());
    }
}
