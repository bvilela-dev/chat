using System.Diagnostics.Metrics;
using BuildingBlocks.Contracts;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PresenceService.Application;
using PresenceService.Domain;
using StackExchange.Redis;

namespace PresenceService.Infrastructure;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

public sealed class PresenceTelemetry : IPresenceTelemetry
{
    private static readonly Meter Meter = new("PresenceService");
    private static readonly Counter<long> Commands = Meter.CreateCounter<long>("presence.commands.total");

    public void RecordCommand(string commandName)
    {
        Commands.Add(1, new KeyValuePair<string, object?>("command", commandName));
    }
}

public sealed class RedisPresenceStore(IConnectionMultiplexer connectionMultiplexer) : IPresenceStore
{
    public async Task<UserPresence> SetOnlineAsync(Guid userId, DateTime occurredAtUtc, CancellationToken cancellationToken)
    {
        var database = connectionMultiplexer.GetDatabase();
        await database.StringSetAsync($"user:{userId}:online", "1");
        return new UserPresence(userId, true, await GetLastSeenAsync(database, userId));
    }

    public async Task<UserPresence> SetOfflineAsync(Guid userId, DateTime occurredAtUtc, CancellationToken cancellationToken)
    {
        var database = connectionMultiplexer.GetDatabase();
        await database.KeyDeleteAsync($"user:{userId}:online");
        await database.StringSetAsync($"user:{userId}:last_seen", occurredAtUtc.ToString("O"));
        return new UserPresence(userId, false, occurredAtUtc);
    }

    public async Task<UserPresence> GetStatusAsync(Guid userId, CancellationToken cancellationToken)
    {
        var database = connectionMultiplexer.GetDatabase();
        var isOnline = await database.KeyExistsAsync($"user:{userId}:online");
        return new UserPresence(userId, isOnline, await GetLastSeenAsync(database, userId));
    }

    public Task<IReadOnlyCollection<UserPresence>> GetOnlineAsync(CancellationToken cancellationToken)
    {
        var endpoint = connectionMultiplexer.GetEndPoints().First();
        var server = connectionMultiplexer.GetServer(endpoint);
        var users = server.Keys(pattern: "user:*:online")
            .Select(key => key.ToString())
            .Select(value => value.Split(':', StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length == 3 && Guid.TryParse(parts[1], out _))
            .Select(parts => new UserPresence(Guid.Parse(parts[1]), true, null))
            .ToArray();

        return Task.FromResult<IReadOnlyCollection<UserPresence>>(users);
    }

    private static async Task<DateTime?> GetLastSeenAsync(IDatabase database, Guid userId)
    {
        var value = await database.StringGetAsync($"user:{userId}:last_seen");
        return DateTime.TryParse(value, out var lastSeenAtUtc) ? lastSeenAtUtc : null;
    }
}

public sealed class PresenceEventPublisher(IPublishEndpoint publishEndpoint) : IPresenceEventPublisher
{
    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : class, IIntegrationEvent
    {
        return publishEndpoint.Publish(integrationEvent, cancellationToken);
    }
}

public static class DependencyInjection
{
    public static IServiceCollection AddPresenceInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPresenceTelemetry, PresenceTelemetry>();
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(configuration.GetConnectionString("Redis") ?? "redis:6379"));
        services.AddSingleton<IPresenceStore, RedisPresenceStore>();
        services.AddScoped<IPresenceEventPublisher, PresenceEventPublisher>();

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