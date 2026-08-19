using BuildingBlocks.Application;
using FluentValidation;
using IdentityService.Application.Abstractions;
using IdentityService.Application.Contracts;
using IdentityService.Domain;
using MediatR;

namespace IdentityService.Application.Users;

/// <summary>Comando de autenticação por e-mail e senha.</summary>
public sealed record LoginUserCommand(string Email, string Password) : IRequest<AuthResponse>;

/// <summary>
/// Regras de formato do login.
/// </summary>
/// <remarks>
/// Deliberadamente mais frouxo que o cadastro: aqui não se valida comprimento
/// mínimo de senha. Se as regras endurecerem no futuro, usuários antigos com
/// senha curta ainda precisam conseguir entrar — e, principalmente, uma
/// mensagem do tipo "senha muito curta" no login confirmaria ao atacante que
/// aquele e-mail existe.
/// </remarks>
public sealed class LoginUserCommandValidator : AbstractValidator<LoginUserCommand>
{
    /// <summary>Configura as regras.</summary>
    public LoginUserCommandValidator()
    {
        RuleFor(command => command.Email).NotEmpty().EmailAddress();
        RuleFor(command => command.Password).NotEmpty();
    }
}

/// <summary>
/// Autentica o usuário e emite um novo par de tokens.
/// </summary>
public sealed class LoginUserCommandHandler(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IClock clock)
    : IRequestHandler<LoginUserCommand, AuthResponse>
{
    /// <summary>Mensagem única para qualquer falha de credencial.</summary>
    /// <remarks>
    /// <para>
    /// <b>Não separe "usuário não existe" de "senha incorreta".</b> Mensagens
    /// distintas transformam o login num oráculo de enumeração: o atacante
    /// submete uma lista de e-mails e descobre, pela resposta, quais estão
    /// cadastrados. Isso alimenta phishing dirigido e credential stuffing.
    /// </para>
    /// </remarks>
    private const string InvalidCredentialsMessage = "E-mail ou senha inválidos.";

    /// <inheritdoc />
    public async Task<AuthResponse> Handle(LoginUserCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = User.NormalizeEmail(request.Email);
        var user = await userRepository.GetByEmailAsync(normalizedEmail, cancellationToken);

        // Ordem importante: a verificação do hash acontece de qualquer forma,
        // mesmo quando o usuário não existe.
        //
        // O motivo é temporização. Com um `return` antecipado, "usuário
        // inexistente" responderia em ~1 ms e "senha errada" em ~100 ms (o custo
        // do BCrypt). Essa diferença é mensurável pela rede e recria o mesmo
        // oráculo de enumeração que a mensagem única tenta evitar. Rodar o hash
        // contra um valor descartável iguala os dois tempos de resposta.
        var passwordMatches = user is not null
            ? passwordHasher.Verify(request.Password, user.PasswordHash)
            : passwordHasher.VerifyAgainstDummyHash(request.Password);

        if (user is null || !passwordMatches)
        {
            throw new UnauthorizedException(InvalidCredentialsMessage);
        }

        var utcNow = clock.UtcNow;
        var tokens = tokenService.CreateTokenPair(user, utcNow);

        // O token é acrescentado ao agregado (regra de domínio) e registrado
        // explicitamente para inclusão (intenção de persistência).
        var refreshToken = user.IssueRefreshToken(tokens.RefreshToken, tokens.RefreshTokenExpiresAtUtc, utcNow);
        userRepository.AddRefreshToken(refreshToken);

        await userRepository.SaveChangesAsync(cancellationToken);

        return new AuthResponse(
            tokens.AccessToken,
            tokens.AccessTokenExpiresAtUtc,
            tokens.RefreshToken,
            tokens.RefreshTokenExpiresAtUtc,
            user.ToDto());
    }
}
