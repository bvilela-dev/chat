using BuildingBlocks.Application;
using BuildingBlocks.Contracts;
using FluentValidation;
using IdentityService.Application.Abstractions;
using IdentityService.Application.Contracts;
using IdentityService.Domain;
using MediatR;

namespace IdentityService.Application.Users;

/// <summary>
/// Comando de cadastro de um novo usuário.
/// </summary>
/// <remarks>
/// Modelado como <c>record</c> por dois motivos: imutabilidade (um comando em
/// trânsito no pipeline não deve ser alterado por um behavior) e igualdade
/// estrutural, que simplifica asserções em teste.
/// </remarks>
public sealed record RegisterUserCommand(string Name, string Email, string Password)
    : IRequest<AuthResponse>;

/// <summary>
/// Regras de formato do cadastro.
/// </summary>
/// <remarks>
/// Executado automaticamente pelo <c>ValidationBehavior</c> antes do handler.
/// Vale reforçar: antes da introdução daquele behavior, este validador estava
/// registrado no contêiner mas <b>nunca era invocado</b> — a API aceitava senha
/// de um caractere.
/// </remarks>
public sealed class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
{
    /// <summary>Configura as regras.</summary>
    public RegisterUserCommandValidator()
    {
        RuleFor(command => command.Name)
            .NotEmpty().WithMessage("Informe o nome.")
            // O limite espelha o `HasMaxLength(128)` do mapeamento EF. Sem esta
            // regra, um nome maior só falharia no INSERT — e o usuário receberia
            // um 500 genérico em vez de um 400 explicando o problema.
            .MaximumLength(128);

        RuleFor(command => command.Email)
            .NotEmpty().WithMessage("Informe o e-mail.")
            .EmailAddress().WithMessage("E-mail em formato inválido.")
            .MaximumLength(256);

        RuleFor(command => command.Password)
            .NotEmpty().WithMessage("Informe a senha.")
            // 8 caracteres é o piso do NIST SP 800-63B. A mesma recomendação
            // desaconselha exigir composição obrigatória (maiúscula, símbolo,
            // dígito): na prática empurra o usuário para padrões previsíveis do
            // tipo "Senha@123". Comprimento vale mais do que complexidade.
            .MinimumLength(8).WithMessage("A senha deve ter ao menos 8 caracteres.")
            // Teto necessário porque o BCrypt trunca a entrada em 72 bytes:
            // aceitar mais criaria a falsa impressão de que a cauda protege algo.
            .MaximumLength(72).WithMessage("A senha deve ter no máximo 72 caracteres.");
    }
}

/// <summary>
/// Executa o cadastro: valida unicidade do e-mail, persiste o usuário, emite os
/// tokens e enfileira o evento de integração.
/// </summary>
public sealed class RegisterUserCommandHandler(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IOutboxWriter outboxWriter,
    IClock clock)
    : IRequestHandler<RegisterUserCommand, AuthResponse>
{
    /// <inheritdoc />
    public async Task<AuthResponse> Handle(RegisterUserCommand request, CancellationToken cancellationToken)
    {
        // Normalizar antes de consultar é essencial: sem isso, "Ana@Teste.com" e
        // "ana@teste.com" passariam pela checagem de unicidade como e-mails
        // distintos, e o índice único do banco rejeitaria o INSERT com um 500.
        var normalizedEmail = User.NormalizeEmail(request.Email);

        if (await userRepository.EmailExistsAsync(normalizedEmail, cancellationToken))
        {
            // Vira 409 no middleware de exceções.
            //
            // Nota de segurança: este endpoint necessariamente revela se um
            // e-mail já está cadastrado — é o preço de dar uma mensagem útil ao
            // usuário. A mitigação correta não é ocultar a informação (o que
            // tornaria o cadastro confuso), e sim o rate limiting aplicado à
            // rota, que impede a enumeração em massa da base.
            throw new ConflictException("Já existe uma conta cadastrada com este e-mail.");
        }

        // Um único instante para todo o caso de uso. Chamar o relógio várias
        // vezes produziria timestamps ligeiramente diferentes para fatos que
        // aconteceram "ao mesmo tempo", atrapalhando a ordenação em auditoria.
        var utcNow = clock.UtcNow;

        var user = User.Register(
            request.Name,
            normalizedEmail,
            passwordHasher.Hash(request.Password),
            utcNow);

        var tokens = tokenService.CreateTokenPair(user, utcNow);
        user.IssueRefreshToken(tokens.RefreshToken, tokens.RefreshTokenExpiresAtUtc, utcNow);

        // Só o usuário é adicionado: o token vem junto, em cascata, por fazer
        // parte do agregado que está sendo inserido.
        await userRepository.AddAsync(user, cancellationToken);

        // Enfileira o evento na MESMA transação da gravação do usuário
        // (ver IOutboxWriter para o porquê).
        outboxWriter.Add(new UserCreatedEvent(
            EventId: Guid.NewGuid(),
            OccurredAtUtc: utcNow,
            UserId: user.Id,
            Name: user.Name,
            Email: user.Email));

        // Um único SaveChanges = uma única transação = usuário + refresh token +
        // linha da outbox são commitados atomicamente.
        await userRepository.SaveChangesAsync(cancellationToken);

        return new AuthResponse(
            tokens.AccessToken,
            tokens.AccessTokenExpiresAtUtc,
            tokens.RefreshToken,
            tokens.RefreshTokenExpiresAtUtc,
            user.ToDto());
    }
}
