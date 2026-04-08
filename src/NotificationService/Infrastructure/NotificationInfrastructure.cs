using System.Diagnostics.Metrics;
using BuildingBlocks.Contracts;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NotificationService.Application;
using StackExchange.Redis;

namespace NotificationService.Infrastructure;

public sealed class NotificationTelemetry : INotificationTelemetry
{
    private static readonly Meter Meter = new("NotificationService");
    private static readonly Counter<long> EventsCounter = Meter.CreateCounter<long>("notification.events.total");

    public void RecordEvent(string eventName)
    {
        EventsCounter.Add(1, new KeyValuePair<string, object?>("event", eventName));
    }
}

public sealed class RedisConversationMembershipStore(IConnectionMultiplexer connectionMultiplexer) : IConversationMembershipStore
{
    public async Task AddParticipantAsync(Guid conversationId, Guid userId, CancellationToken cancellationToken)
    {
        var database = connectionMultiplexer.GetDatabase();
        await database.SetAddAsync($"conversation:{conversationId}:participants", userId.ToString());
    }

    public async Task RemoveParticipantAsync(Guid conversationId, Guid userId, CancellationToken cancellationToken)
    {
        var database = connectionMultiplexer.GetDatabase();
        await database.SetRemoveAsync($"conversation:{conversationId}:participants", userId.ToString());
    }

    public async Task<IReadOnlyCollection<Guid>> GetParticipantsAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var database = connectionMultiplexer.GetDatabase();
        var members = await database.SetMembersAsync($"conversation:{conversationId}:participants");
        return members.Select(value => Guid.TryParse(value, out var userId) ? userId : Guid.Empty)
            .Where(userId => userId != Guid.Empty)
            .ToArray();
    }

    public async Task<bool> HasProcessedAsync(Guid eventId, string consumerName, CancellationToken cancellationToken)
    {
        var database = connectionMultiplexer.GetDatabase();
        return await database.KeyExistsAsync($"notification:inbox:{consumerName}:{eventId}");
    }

    public Task MarkProcessedAsync(Guid eventId, string consumerName, CancellationToken cancellationToken)
    {
        var database = connectionMultiplexer.GetDatabase();
        return database.StringSetAsync($"notification:inbox:{consumerName}:{eventId}", "1", TimeSpan.FromDays(7));
    }
}

public sealed class RedisPresenceLookup(IConnectionMultiplexer connectionMultiplexer) : IPresenceLookup
{
    public async Task<bool> IsOnlineAsync(Guid userId, CancellationToken cancellationToken)
    {
        var database = connectionMultiplexer.GetDatabase();
        return await database.KeyExistsAsync($"user:{userId}:online");
    }
}

public sealed class LoggingNotificationSender(ILogger<LoggingNotificationSender> logger) : INotificationSender
{
    public Task SendPushAsync(Guid userId, string message, CancellationToken cancellationToken)
    {
        logger.LogInformation("Mock push notification sent to user {UserId}: {Message}", userId, message);
        return Task.CompletedTask;
    }

    public Task SendEmailAsync(Guid userId, string subject, string message, CancellationToken cancellationToken)
    {
        logger.LogInformation("Mock email sent to user {UserId} with subject {Subject}: {Message}", userId, subject, message);
        return Task.CompletedTask;
    }
}

public sealed class MessageSentConsumer(IConversationMembershipStore membershipStore, ISender sender) : IConsumer<MessageSentEvent>
{
    public async Task Consume(ConsumeContext<MessageSentEvent> context)
    {
        const string consumerName = nameof(MessageSentConsumer);
        if (await membershipStore.HasProcessedAsync(context.Message.EventId, consumerName, context.CancellationToken))
        {
            return;
        }

        await sender.Send(new NotifyOfflineUsersCommand(context.Message), context.CancellationToken);
        await membershipStore.MarkProcessedAsync(context.Message.EventId, consumerName, context.CancellationToken);
    }
}

public sealed class ConversationJoinedConsumer(ISender sender) : IConsumer<ConversationJoinedEvent>
{
    public Task Consume(ConsumeContext<ConversationJoinedEvent> context)
    {
        return sender.Send(new TrackConversationParticipantCommand(context.Message.EventId, nameof(ConversationJoinedConsumer), context.Message.ConversationId, context.Message.UserId, true), context.CancellationToken);
    }
}

public sealed class ConversationLeftConsumer(ISender sender) : IConsumer<ConversationLeftEvent>
{
    public Task Consume(ConsumeContext<ConversationLeftEvent> context)
    {
        return sender.Send(new TrackConversationParticipantCommand(context.Message.EventId, nameof(ConversationLeftConsumer), context.Message.ConversationId, context.Message.UserId, false), context.CancellationToken);
    }
}

public static class DependencyInjection
{
    public static IServiceCollection AddNotificationInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<INotificationTelemetry, NotificationTelemetry>();
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(configuration.GetConnectionString("Redis") ?? "redis:6379"));
        services.AddSingleton<IConversationMembershipStore, RedisConversationMembershipStore>();
        services.AddSingleton<IPresenceLookup, RedisPresenceLookup>();
        services.AddSingleton<INotificationSender, LoggingNotificationSender>();

        services.AddMassTransit(configurationBuilder =>
        {
            configurationBuilder.AddConsumer<MessageSentConsumer>();
            configurationBuilder.AddConsumer<ConversationJoinedConsumer>();
            configurationBuilder.AddConsumer<ConversationLeftConsumer>();
            configurationBuilder.SetKebabCaseEndpointNameFormatter();

            configurationBuilder.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(configuration["RabbitMq:Host"] ?? "localhost", "/", host =>
                {
                    host.Username(configuration["RabbitMq:Username"] ?? "guest");
                    host.Password(configuration["RabbitMq:Password"] ?? "guest");
                });

                cfg.ReceiveEndpoint(MessagingConstants.NotificationQueue, endpoint =>
                {
                    ConfigureEndpoint(endpoint);
                    endpoint.ConfigureConsumer<MessageSentConsumer>(context);
                });

                cfg.ReceiveEndpoint("notification.conversation-joined", endpoint =>
                {
                    ConfigureEndpoint(endpoint);
                    endpoint.ConfigureConsumer<ConversationJoinedConsumer>(context);
                });

                cfg.ReceiveEndpoint("notification.conversation-left", endpoint =>
                {
                    ConfigureEndpoint(endpoint);
                    endpoint.ConfigureConsumer<ConversationLeftConsumer>(context);
                });
            });
        });

        return services;
    }

    private static void ConfigureEndpoint(IRabbitMqReceiveEndpointConfigurator endpoint)
    {
        endpoint.SetQueueArgument("x-dead-letter-exchange", "chat.dlx");
        endpoint.UseMessageRetry(retry => retry.Exponential(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(2)));
        endpoint.UseCircuitBreaker(breaker =>
        {
            breaker.ActiveThreshold = 5;
            breaker.TrackingPeriod = TimeSpan.FromMinutes(1);
            breaker.ResetInterval = TimeSpan.FromMinutes(1);
            breaker.TripThreshold = 15;
        });
    }
}