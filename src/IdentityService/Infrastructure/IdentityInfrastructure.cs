using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using BuildingBlocks.Contracts;
using IdentityService.Application;
using IdentityService.Domain;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace IdentityService.Infrastructure;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(builder =>
        {
            builder.ToTable("users");
            builder.HasKey(entity => entity.Id);
            builder.Property(entity => entity.Name).HasMaxLength(128).IsRequired();
            builder.Property(entity => entity.Email).HasMaxLength(256).IsRequired();
            builder.HasIndex(entity => entity.Email).IsUnique();
            builder.Property(entity => entity.PasswordHash).IsRequired();
            builder.Property(entity => entity.CreatedAtUtc).IsRequired();
            builder.HasMany(entity => entity.RefreshTokens)
                .WithOne()
                .HasForeignKey(token => token.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RefreshToken>(builder =>
        {
            builder.ToTable("refresh_tokens");
            builder.HasKey(entity => entity.Id);
            builder.Property(entity => entity.Token).HasMaxLength(512).IsRequired();
            builder.HasIndex(entity => entity.Token).IsUnique();
            builder.Property(entity => entity.ExpiresAtUtc).IsRequired();
            builder.Property(entity => entity.CreatedAtUtc).IsRequired();
        });

        modelBuilder.Entity<OutboxMessage>(builder =>
        {
            builder.ToTable("outbox_messages");
            builder.HasKey(entity => entity.Id);
            builder.Property(entity => entity.Type).HasMaxLength(256).IsRequired();
            builder.Property(entity => entity.Payload).HasColumnType("jsonb").IsRequired();
            builder.Property(entity => entity.OccurredOnUtc).IsRequired();
        });
    }
}

public sealed class IdentityDbContextFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<IdentityDbContext>();
        builder.UseNpgsql("Host=localhost;Port=5432;Database=chat_identity;Username=postgres;Password=postgres");
        return new IdentityDbContext(builder.Options);
    }
}

public sealed class UserRepository(IdentityDbContext dbContext) : IUserRepository
{
    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken)
    {
        return dbContext.Users.AnyAsync(user => user.Email == email, cancellationToken);
    }

    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken)
    {
        return dbContext.Users.Include(user => user.RefreshTokens).SingleOrDefaultAsync(user => user.Email == email, cancellationToken);
    }

    public Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        return dbContext.Users.Include(user => user.RefreshTokens).SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);
    }

    public Task<User?> GetByRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        return dbContext.Users.Include(user => user.RefreshTokens)
            .SingleOrDefaultAsync(user => user.RefreshTokens.Any(token => token.Token == refreshToken), cancellationToken);
    }

    public Task AddAsync(User user, CancellationToken cancellationToken)
    {
        return dbContext.Users.AddAsync(user, cancellationToken).AsTask();
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}

public sealed class BcryptPasswordHasher : IPasswordHasher
{
    public string Hash(string value)
    {
        return BCrypt.Net.BCrypt.HashPassword(value);
    }

    public bool Verify(string value, string hash)
    {
        return BCrypt.Net.BCrypt.Verify(value, hash);
    }
}

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = "chat-identity";

    public string Audience { get; init; } = "chat-clients";

    public string Key { get; init; } = "super-secret-development-key-change-me";

    public int AccessTokenMinutes { get; init; } = 15;

    public int RefreshTokenDays { get; init; } = 7;
}

public sealed class JwtTokenService(Microsoft.Extensions.Options.IOptions<JwtOptions> options) : ITokenService
{
    public TokenPair CreateTokenPair(User user, DateTime utcNow)
    {
        var jwtOptions = options.Value;
        var accessTokenExpiresAtUtc = utcNow.AddMinutes(jwtOptions.AccessTokenMinutes);
        var refreshTokenExpiresAtUtc = utcNow.AddDays(jwtOptions.RefreshTokenDays);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Name, user.Name),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Name)
        };

        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)), SecurityAlgorithms.HmacSha256);
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = accessTokenExpiresAtUtc,
            Issuer = jwtOptions.Issuer,
            Audience = jwtOptions.Audience,
            SigningCredentials = credentials
        };

        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(descriptor);

        return new TokenPair(handler.WriteToken(token), accessTokenExpiresAtUtc, Convert.ToBase64String(Guid.NewGuid().ToByteArray()) + Convert.ToBase64String(Guid.NewGuid().ToByteArray()), refreshTokenExpiresAtUtc);
    }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

public sealed class EfOutboxWriter(IdentityDbContext dbContext) : IOutboxWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public void Add(IIntegrationEvent integrationEvent)
    {
        var outboxMessage = OutboxMessage.Create(integrationEvent.EventId, integrationEvent.GetType().Name, JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), SerializerOptions), integrationEvent.OccurredAtUtc);
        dbContext.OutboxMessages.Add(outboxMessage);
    }
}

public sealed class IdentityOutboxDispatcher(IServiceScopeFactory serviceScopeFactory, ILogger<IdentityOutboxDispatcher> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchBatchAsync(stoppingToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Identity outbox dispatch failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task DispatchBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var messages = await dbContext.OutboxMessages
            .Where(message => message.ProcessedOnUtc == null)
            .OrderBy(message => message.OccurredOnUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            try
            {
                if (message.Type == nameof(UserCreatedEvent))
                {
                    var integrationEvent = JsonSerializer.Deserialize<UserCreatedEvent>(message.Payload, SerializerOptions)
                        ?? throw new InvalidOperationException("Unable to deserialize outbox payload.");
                    await publishEndpoint.Publish(integrationEvent, cancellationToken);
                }

                message.MarkProcessed(clock.UtcNow);
            }
            catch (Exception exception)
            {
                message.MarkFailed(exception.Message);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<IdentityDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("IdentityDatabase")));

        services.AddSingleton<Microsoft.Extensions.Options.IOptions<JwtOptions>>(_ =>
            Microsoft.Extensions.Options.Options.Create(new JwtOptions
            {
                Issuer = configuration[$"{JwtOptions.SectionName}:Issuer"] ?? "chat-identity",
                Audience = configuration[$"{JwtOptions.SectionName}:Audience"] ?? "chat-clients",
                Key = configuration[$"{JwtOptions.SectionName}:Key"] ?? "super-secret-development-key-change-me",
                AccessTokenMinutes = int.TryParse(configuration[$"{JwtOptions.SectionName}:AccessTokenMinutes"], out var accessTokenMinutes) ? accessTokenMinutes : 15,
                RefreshTokenDays = int.TryParse(configuration[$"{JwtOptions.SectionName}:RefreshTokenDays"], out var refreshTokenDays) ? refreshTokenDays : 7
            }));

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IOutboxWriter, EfOutboxWriter>();
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddHostedService<IdentityOutboxDispatcher>();

        services.AddMassTransit(configurationBuilder =>
        {
            configurationBuilder.SetKebabCaseEndpointNameFormatter();
            configurationBuilder.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(configuration["RabbitMq:Host"] ?? "localhost", "/", host =>
                {
                    host.Username(configuration["RabbitMq:Username"] ?? "guest");
                    host.Password(configuration["RabbitMq:Password"] ?? "guest");
                });

                cfg.UseMessageRetry(retry => retry.Exponential(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(2)));
                cfg.UseCircuitBreaker(breaker =>
                {
                    breaker.ActiveThreshold = 5;
                    breaker.TrackingPeriod = TimeSpan.FromMinutes(1);
                    breaker.ResetInterval = TimeSpan.FromMinutes(1);
                    breaker.TripThreshold = 15;
                });
                cfg.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}