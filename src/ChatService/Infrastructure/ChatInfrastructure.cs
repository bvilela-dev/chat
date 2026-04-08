using System.Diagnostics.Metrics;
using System.Net.Http;
using BuildingBlocks.Contracts;
using BuildingBlocks.Contracts.Grpc;
using ChatService.Application;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using StackExchange.Redis;

namespace ChatService.Infrastructure;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

public sealed class IdentityValidationClient(UserValidationGrpc.UserValidationGrpcClient client) : IIdentityValidationClient
{
    public async Task<ValidatedUser?> ValidateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var response = await client.ValidateUserAsync(new ValidateUserRequest { UserId = userId.ToString() }, cancellationToken: cancellationToken);
        if (!response.Exists || !Guid.TryParse(response.UserId, out var parsedUserId))
        {
            return null;
        }

        return new ValidatedUser(parsedUserId, response.Name, response.Email);
    }
}

public sealed class RedisConnectionRegistry(IConnectionMultiplexer connectionMultiplexer) : IConnectionRegistry
{
    public async Task RegisterConnectionAsync(Guid userId, string connectionId, CancellationToken cancellationToken)
    {
        var database = connectionMultiplexer.GetDatabase();
        await database.SetAddAsync($"user:{userId}:connections", connectionId);
        await database.StringSetAsync($"connection:{connectionId}:user", userId.ToString());
    }

    public async Task RemoveConnectionAsync(Guid userId, string connectionId, CancellationToken cancellationToken)
    {
        var database = connectionMultiplexer.GetDatabase();
        await database.SetRemoveAsync($"user:{userId}:connections", connectionId);
        await database.KeyDeleteAsync($"connection:{connectionId}:user");
    }
}

public sealed class ChatEventPublisher(IPublishEndpoint publishEndpoint) : IChatEventPublisher
{
    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : class, IIntegrationEvent
    {
        return publishEndpoint.Publish(integrationEvent, cancellationToken);
    }
}

public sealed class ChatTelemetry : IChatTelemetry
{
    private static readonly Meter Meter = new("ChatService");
    private static readonly Counter<long> CommandCounter = Meter.CreateCounter<long>("chat.commands.total");
    private static long _activeConnections;
    private static readonly ObservableGauge<long> ConnectionsGauge = Meter.CreateObservableGauge("chat.signalr.connections", () => _activeConnections);

    public void IncrementCommand(string commandName)
    {
        CommandCounter.Add(1, new KeyValuePair<string, object?>("command", commandName));
    }

    public void ConnectionOpened()
    {
        Interlocked.Increment(ref _activeConnections);
    }

    public void ConnectionClosed()
    {
        Interlocked.Decrement(ref _activeConnections);
    }
}

public static class DependencyInjection
{
    public static IServiceCollection AddChatInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IChatTelemetry, ChatTelemetry>();
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(configuration.GetConnectionString("Redis") ?? "redis:6379"));
        services.AddSingleton<IConnectionRegistry, RedisConnectionRegistry>();
        services.AddScoped<IIdentityValidationClient, IdentityValidationClient>();
        services.AddScoped<IChatEventPublisher, ChatEventPublisher>();

        services.AddGrpcClient<UserValidationGrpc.UserValidationGrpcClient>(options =>
            {
                options.Address = new Uri(configuration["Grpc:Identity"] ?? "http://identity-service:8080");
            })
            .AddPolicyHandler(_ => Policy<HttpResponseMessage>.Handle<Exception>().WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt))))
            .AddPolicyHandler(_ => Policy<HttpResponseMessage>.Handle<Exception>().CircuitBreakerAsync(5, TimeSpan.FromMinutes(1)));

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