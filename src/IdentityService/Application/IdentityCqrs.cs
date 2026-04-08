using AutoMapper;
using BuildingBlocks.Contracts;
using FluentValidation;
using IdentityService.Domain;
using MediatR;

namespace IdentityService.Application;

public sealed record UserDto(Guid Id, string Name, string Email, DateTime CreatedAtUtc);

public sealed record AuthResponse(string AccessToken, DateTime AccessTokenExpiresAtUtc, string RefreshToken, DateTime RefreshTokenExpiresAtUtc, UserDto User);

public sealed record TokenPair(string AccessToken, DateTime AccessTokenExpiresAtUtc, string RefreshToken, DateTime RefreshTokenExpiresAtUtc);

public sealed record RegisterUserCommand(string Name, string Email, string Password) : IRequest<AuthResponse>;

public sealed record LoginUserCommand(string Email, string Password) : IRequest<AuthResponse>;

public sealed record RefreshTokenCommand(string RefreshToken) : IRequest<AuthResponse>;

public sealed record GetUserByIdQuery(Guid UserId) : IRequest<UserDto?>;

public interface IUserRepository
{
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken);

    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken);

    Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<User?> GetByRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken);

    Task AddAsync(User user, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IPasswordHasher
{
    string Hash(string value);

    bool Verify(string value, string hash);
}

public interface ITokenService
{
    TokenPair CreateTokenPair(User user, DateTime utcNow);
}

public interface IOutboxWriter
{
    void Add(IIntegrationEvent integrationEvent);
}

public interface IClock
{
    DateTime UtcNow { get; }
}

public sealed class IdentityMappingProfile : Profile
{
    public IdentityMappingProfile()
    {
        CreateMap<User, UserDto>()
            .ForCtorParam(nameof(UserDto.CreatedAtUtc), options => options.MapFrom(source => source.CreatedAtUtc));
    }
}

public sealed class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
{
    public RegisterUserCommandValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(128);
        RuleFor(command => command.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(command => command.Password).NotEmpty().MinimumLength(8);
    }
}

public sealed class LoginUserCommandValidator : AbstractValidator<LoginUserCommand>
{
    public LoginUserCommandValidator()
    {
        RuleFor(command => command.Email).NotEmpty().EmailAddress();
        RuleFor(command => command.Password).NotEmpty();
    }
}

public sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
    {
        RuleFor(command => command.RefreshToken).NotEmpty();
    }
}

public sealed class RegisterUserCommandHandler(IUserRepository userRepository, IPasswordHasher passwordHasher, ITokenService tokenService, IOutboxWriter outboxWriter, IMapper mapper, IClock clock)
    : IRequestHandler<RegisterUserCommand, AuthResponse>
{
    public async Task<AuthResponse> Handle(RegisterUserCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        if (await userRepository.EmailExistsAsync(normalizedEmail, cancellationToken))
        {
            throw new ConflictException("A user with this email already exists.");
        }

        var utcNow = clock.UtcNow;
        var user = User.Register(request.Name, normalizedEmail, passwordHasher.Hash(request.Password), utcNow);
        var tokens = tokenService.CreateTokenPair(user, utcNow);
        user.IssueRefreshToken(tokens.RefreshToken, tokens.RefreshTokenExpiresAtUtc, utcNow);

        await userRepository.AddAsync(user, cancellationToken);
        outboxWriter.Add(new UserCreatedEvent(Guid.NewGuid(), utcNow, user.Id, user.Name, user.Email));
        await userRepository.SaveChangesAsync(cancellationToken);

        return new AuthResponse(tokens.AccessToken, tokens.AccessTokenExpiresAtUtc, tokens.RefreshToken, tokens.RefreshTokenExpiresAtUtc, mapper.Map<UserDto>(user));
    }
}

public sealed class LoginUserCommandHandler(IUserRepository userRepository, IPasswordHasher passwordHasher, ITokenService tokenService, IMapper mapper, IClock clock)
    : IRequestHandler<LoginUserCommand, AuthResponse>
{
    public async Task<AuthResponse> Handle(LoginUserCommand request, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByEmailAsync(request.Email.Trim().ToLowerInvariant(), cancellationToken)
            ?? throw new UnauthorizedException("Invalid email or password.");

        if (!passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedException("Invalid email or password.");
        }

        var utcNow = clock.UtcNow;
        var tokens = tokenService.CreateTokenPair(user, utcNow);
        user.IssueRefreshToken(tokens.RefreshToken, tokens.RefreshTokenExpiresAtUtc, utcNow);
        await userRepository.SaveChangesAsync(cancellationToken);

        return new AuthResponse(tokens.AccessToken, tokens.AccessTokenExpiresAtUtc, tokens.RefreshToken, tokens.RefreshTokenExpiresAtUtc, mapper.Map<UserDto>(user));
    }
}

public sealed class RefreshTokenCommandHandler(IUserRepository userRepository, ITokenService tokenService, IMapper mapper, IClock clock)
    : IRequestHandler<RefreshTokenCommand, AuthResponse>
{
    public async Task<AuthResponse> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByRefreshTokenAsync(request.RefreshToken, cancellationToken)
            ?? throw new UnauthorizedException("Refresh token is invalid.");

        var utcNow = clock.UtcNow;
        var activeRefreshToken = user.GetActiveRefreshToken(request.RefreshToken, utcNow)
            ?? throw new UnauthorizedException("Refresh token is expired or revoked.");

        activeRefreshToken.Revoke(utcNow);
        var tokens = tokenService.CreateTokenPair(user, utcNow);
        user.IssueRefreshToken(tokens.RefreshToken, tokens.RefreshTokenExpiresAtUtc, utcNow);
        await userRepository.SaveChangesAsync(cancellationToken);

        return new AuthResponse(tokens.AccessToken, tokens.AccessTokenExpiresAtUtc, tokens.RefreshToken, tokens.RefreshTokenExpiresAtUtc, mapper.Map<UserDto>(user));
    }
}

public sealed class GetUserByIdQueryHandler(IUserRepository userRepository, IMapper mapper) : IRequestHandler<GetUserByIdQuery, UserDto?>
{
    public async Task<UserDto?> Handle(GetUserByIdQuery request, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(request.UserId, cancellationToken);
        return user is null ? null : mapper.Map<UserDto>(user);
    }
}

public sealed class ConflictException(string message) : Exception(message);

public sealed class UnauthorizedException(string message) : Exception(message);