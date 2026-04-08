using System.Diagnostics.Metrics;
using System.Text.Json;
using BuildingBlocks.Contracts;
using MassTransit;
using MediatR;
using MessageService.Application;
using MessageService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MessageService.Infrastructure;

public sealed class MessageDbContext(DbContextOptions<MessageDbContext> options) : DbContext(options)
{
    public DbSet<Conversation> Conversations => Set<Conversation>();

    public DbSet<Message> Messages => Set<Message>();

    public DbSet<ConversationParticipant> ConversationParticipants => Set<ConversationParticipant>();

    public DbSet<MessageReadModel> MessageReadModels => Set<MessageReadModel>();

    public DbSet<ConversationReadModel> ConversationReadModels => Set<ConversationReadModel>();

    public DbSet<ConversationParticipantReadModel> ConversationParticipantReadModels => Set<ConversationParticipantReadModel>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Conversation>(builder =>
        {
            builder.ToTable("conversations");
            builder.HasKey(entity => entity.Id);
        });

        modelBuilder.Entity<Message>(builder =>
        {
            builder.ToTable("messages");
            builder.HasKey(entity => entity.Id);
            builder.HasIndex(entity => entity.ConversationId);
            builder.Property(entity => entity.SenderName).HasMaxLength(256).IsRequired();
            builder.Property(entity => entity.Content).HasMaxLength(4000).IsRequired();
        });

        modelBuilder.Entity<ConversationParticipant>(builder =>
        {
            builder.ToTable("conversation_participants");
            builder.HasKey(entity => new { entity.ConversationId, entity.UserId });
        });

        modelBuilder.Entity<MessageReadModel>(builder =>
        {
            builder.ToTable("message_read_models");
            builder.HasKey(entity => entity.Id);
            builder.HasIndex(entity => entity.ConversationId);
            builder.Property(entity => entity.Content).HasMaxLength(4000).IsRequired();
            builder.Property(entity => entity.SenderName).HasMaxLength(256).IsRequired();
        });

        modelBuilder.Entity<ConversationReadModel>(builder =>
        {
            builder.ToTable("conversation_read_models");
            builder.HasKey(entity => entity.Id);
            builder.Property(entity => entity.LastMessage).HasMaxLength(4000).IsRequired();
        });

        modelBuilder.Entity<ConversationParticipantReadModel>(builder =>
        {
            builder.ToTable("conversation_participant_read_models");
            builder.HasKey(entity => new { entity.ConversationId, entity.UserId });
        });

        modelBuilder.Entity<OutboxMessage>(builder =>
        {
            builder.ToTable("outbox_messages");
            builder.HasKey(entity => entity.Id);
            builder.Property(entity => entity.Payload).HasColumnType("jsonb").IsRequired();
            builder.Property(entity => entity.Type).HasMaxLength(256).IsRequired();
        });

        modelBuilder.Entity<InboxMessage>(builder =>
        {
            builder.ToTable("inbox_messages");
            builder.HasKey(entity => new { entity.EventId, entity.ConsumerName });
            builder.Property(entity => entity.ConsumerName).HasMaxLength(256).IsRequired();
        });
    }
}

public sealed class MessageDbContextFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<MessageDbContext>
{
    public MessageDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<MessageDbContext>();
        builder.UseNpgsql("Host=localhost;Port=5432;Database=chat_message;Username=postgres;Password=postgres");
        return new MessageDbContext(builder.Options);
    }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

public sealed class MessageTelemetry : IMessageTelemetry
{
    private static readonly Meter Meter = new("MessageService");
    private static readonly Counter<long> EventsCounter = Meter.CreateCounter<long>("message.events.total");
    private static readonly Histogram<double> ProjectionLagMs = Meter.CreateHistogram<double>("message.projection.lag.ms");

    public void RecordConsumedEvent(string eventName)
    {
        EventsCounter.Add(1, new KeyValuePair<string, object?>("event", eventName));
    }

    public void RecordProjectionLag(TimeSpan lag)
    {
        ProjectionLagMs.Record(lag.TotalMilliseconds);
    }
}

public sealed class MessageRepository(MessageDbContext dbContext) : IMessageRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public Task<bool> HasProcessedAsync(Guid eventId, string consumerName, CancellationToken cancellationToken)
    {
        return dbContext.InboxMessages.AnyAsync(message => message.EventId == eventId && message.ConsumerName == consumerName, cancellationToken);
    }

    public async Task MarkProcessedAsync(Guid eventId, string consumerName, CancellationToken cancellationToken)
    {
        if (!await HasProcessedAsync(eventId, consumerName, cancellationToken))
        {
            await dbContext.InboxMessages.AddAsync(InboxMessage.Create(eventId, consumerName, DateTime.UtcNow), cancellationToken);
        }
    }

    public Task<bool> MessageExistsAsync(Guid messageId, CancellationToken cancellationToken)
    {
        return dbContext.Messages.AnyAsync(message => message.Id == messageId, cancellationToken);
    }

    public Task<Conversation?> GetConversationAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        return dbContext.Conversations.SingleOrDefaultAsync(conversation => conversation.Id == conversationId, cancellationToken);
    }

    public Task AddConversationAsync(Conversation conversation, CancellationToken cancellationToken)
    {
        return dbContext.Conversations.AddAsync(conversation, cancellationToken).AsTask();
    }

    public Task<bool> HasParticipantAsync(Guid conversationId, Guid userId, CancellationToken cancellationToken)
    {
        return dbContext.ConversationParticipants.AnyAsync(entity => entity.ConversationId == conversationId && entity.UserId == userId, cancellationToken);
    }

    public async Task AddParticipantAsync(Guid conversationId, Guid userId, DateTime joinedAtUtc, CancellationToken cancellationToken)
    {
        if (!await HasParticipantAsync(conversationId, userId, cancellationToken))
        {
            await dbContext.ConversationParticipants.AddAsync(ConversationParticipant.Create(conversationId, userId, joinedAtUtc), cancellationToken);
        }

        if (!await dbContext.ConversationParticipantReadModels.AnyAsync(entity => entity.ConversationId == conversationId && entity.UserId == userId, cancellationToken))
        {
            await dbContext.ConversationParticipantReadModels.AddAsync(ConversationParticipantReadModel.Create(conversationId, userId), cancellationToken);
        }
    }

    public async Task RemoveParticipantAsync(Guid conversationId, Guid userId, CancellationToken cancellationToken)
    {
        var participant = await dbContext.ConversationParticipants.SingleOrDefaultAsync(entity => entity.ConversationId == conversationId && entity.UserId == userId, cancellationToken);
        if (participant is not null)
        {
            dbContext.ConversationParticipants.Remove(participant);
        }

        var readParticipant = await dbContext.ConversationParticipantReadModels.SingleOrDefaultAsync(entity => entity.ConversationId == conversationId && entity.UserId == userId, cancellationToken);
        if (readParticipant is not null)
        {
            dbContext.ConversationParticipantReadModels.Remove(readParticipant);
        }
    }

    public void AddMessage(Message message)
    {
        dbContext.Messages.Add(message);
    }

    public void EnqueueOutbox(IIntegrationEvent integrationEvent)
    {
        dbContext.OutboxMessages.Add(OutboxMessage.Create(integrationEvent.EventId, integrationEvent.GetType().Name, JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), SerializerOptions), integrationEvent.OccurredAtUtc));
    }

    public async Task UpsertProjectionAsync(MessageProjectionRequested projection, CancellationToken cancellationToken)
    {
        if (!await dbContext.MessageReadModels.AnyAsync(entity => entity.Id == projection.MessageId, cancellationToken))
        {
            await dbContext.MessageReadModels.AddAsync(MessageReadModel.Create(projection.MessageId, projection.ConversationId, projection.SenderId, projection.SenderName, projection.Content, projection.MessageCreatedAtUtc), cancellationToken);
        }

        var conversation = await dbContext.ConversationReadModels.SingleOrDefaultAsync(entity => entity.Id == projection.ConversationId, cancellationToken);
        if (conversation is null)
        {
            await dbContext.ConversationReadModels.AddAsync(ConversationReadModel.Create(projection.ConversationId, projection.Content, projection.MessageCreatedAtUtc), cancellationToken);
        }
        else
        {
            conversation.Update(projection.Content, projection.MessageCreatedAtUtc);
        }

        if (!await dbContext.ConversationParticipantReadModels.AnyAsync(entity => entity.ConversationId == projection.ConversationId && entity.UserId == projection.SenderId, cancellationToken))
        {
            await dbContext.ConversationParticipantReadModels.AddAsync(ConversationParticipantReadModel.Create(projection.ConversationId, projection.SenderId), cancellationToken);
        }
    }

    public async Task<IReadOnlyCollection<MessageReadModel>> GetMessagesByConversationAsync(Guid conversationId, int page, int pageSize, CancellationToken cancellationToken)
    {
        return await dbContext.MessageReadModels
            .Where(entity => entity.ConversationId == conversationId)
            .OrderBy(entity => entity.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<ConversationReadModel>> GetUserConversationsAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await dbContext.ConversationParticipantReadModels
            .Where(entity => entity.UserId == userId)
            .Join(dbContext.ConversationReadModels,
                participant => participant.ConversationId,
                conversation => conversation.Id,
                (_, conversation) => conversation)
            .OrderByDescending(entity => entity.LastMessageAtUtc)
            .ToListAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}

public sealed class MessageOutboxDispatcher(IServiceScopeFactory serviceScopeFactory, ILogger<MessageOutboxDispatcher> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = serviceScopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<MessageDbContext>();
                var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

                var messages = await dbContext.OutboxMessages
                    .Where(message => message.ProcessedOnUtc == null)
                    .OrderBy(message => message.OccurredOnUtc)
                    .Take(50)
                    .ToListAsync(stoppingToken);

                foreach (var message in messages)
                {
                    try
                    {
                        if (message.Type == nameof(MessageProjectionRequested))
                        {
                            var projection = JsonSerializer.Deserialize<MessageProjectionRequested>(message.Payload, SerializerOptions)
                                ?? throw new InvalidOperationException("Unable to deserialize message projection payload.");
                            await publishEndpoint.Publish(projection, stoppingToken);
                        }

                        message.MarkProcessed(DateTime.UtcNow);
                    }
                    catch (Exception exception)
                    {
                        logger.LogError(exception, "Failed to publish outbox message {OutboxMessageId}.", message.Id);
                        message.MarkFailed(exception.Message);
                    }
                }

                await dbContext.SaveChangesAsync(stoppingToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Message outbox dispatcher failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}

public sealed class MessageSentConsumer(IMessageRepository repository, ISender sender) : IConsumer<MessageSentEvent>
{
    public async Task Consume(ConsumeContext<MessageSentEvent> context)
    {
        const string consumerName = nameof(MessageSentConsumer);
        if (await repository.HasProcessedAsync(context.Message.EventId, consumerName, context.CancellationToken))
        {
            return;
        }

        await sender.Send(new PersistMessageCommand(context.Message), context.CancellationToken);
        await repository.MarkProcessedAsync(context.Message.EventId, consumerName, context.CancellationToken);
        await repository.SaveChangesAsync(context.CancellationToken);
    }
}

public sealed class ConversationJoinedConsumer(IMessageRepository repository, ISender sender) : IConsumer<ConversationJoinedEvent>
{
    public async Task Consume(ConsumeContext<ConversationJoinedEvent> context)
    {
        const string consumerName = nameof(ConversationJoinedConsumer);
        if (await repository.HasProcessedAsync(context.Message.EventId, consumerName, context.CancellationToken))
        {
            return;
        }

        await sender.Send(new UpdateConversationMembershipCommand(context.Message.EventId, consumerName, context.Message.ConversationId, context.Message.UserId, true), context.CancellationToken);
        await repository.MarkProcessedAsync(context.Message.EventId, consumerName, context.CancellationToken);
        await repository.SaveChangesAsync(context.CancellationToken);
    }
}

public sealed class ConversationLeftConsumer(IMessageRepository repository, ISender sender) : IConsumer<ConversationLeftEvent>
{
    public async Task Consume(ConsumeContext<ConversationLeftEvent> context)
    {
        const string consumerName = nameof(ConversationLeftConsumer);
        if (await repository.HasProcessedAsync(context.Message.EventId, consumerName, context.CancellationToken))
        {
            return;
        }

        await sender.Send(new UpdateConversationMembershipCommand(context.Message.EventId, consumerName, context.Message.ConversationId, context.Message.UserId, false), context.CancellationToken);
        await repository.MarkProcessedAsync(context.Message.EventId, consumerName, context.CancellationToken);
        await repository.SaveChangesAsync(context.CancellationToken);
    }
}

public sealed class MessageProjectionRequestedConsumer(IMessageRepository repository, ISender sender) : IConsumer<MessageProjectionRequested>
{
    public async Task Consume(ConsumeContext<MessageProjectionRequested> context)
    {
        const string consumerName = nameof(MessageProjectionRequestedConsumer);
        if (await repository.HasProcessedAsync(context.Message.EventId, consumerName, context.CancellationToken))
        {
            return;
        }

        await sender.Send(new ProjectMessageReadModelCommand(context.Message), context.CancellationToken);
        await repository.MarkProcessedAsync(context.Message.EventId, consumerName, context.CancellationToken);
        await repository.SaveChangesAsync(context.CancellationToken);
    }
}

public static class DependencyInjection
{
    public static IServiceCollection AddMessageInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<MessageDbContext>(options => options.UseNpgsql(configuration.GetConnectionString("MessageDatabase")));
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IMessageTelemetry, MessageTelemetry>();
        services.AddHostedService<MessageOutboxDispatcher>();

        services.AddMassTransit(configurationBuilder =>
        {
            configurationBuilder.AddConsumer<MessageSentConsumer>();
            configurationBuilder.AddConsumer<ConversationJoinedConsumer>();
            configurationBuilder.AddConsumer<ConversationLeftConsumer>();
            configurationBuilder.AddConsumer<MessageProjectionRequestedConsumer>();
            configurationBuilder.SetKebabCaseEndpointNameFormatter();

            configurationBuilder.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(configuration["RabbitMq:Host"] ?? "localhost", "/", host =>
                {
                    host.Username(configuration["RabbitMq:Username"] ?? "guest");
                    host.Password(configuration["RabbitMq:Password"] ?? "guest");
                });

                cfg.ReceiveEndpoint(MessagingConstants.ChatPersistQueue, endpoint =>
                {
                    ConfigureEndpoint(endpoint);
                    endpoint.ConfigureConsumer<MessageSentConsumer>(context);
                });

                cfg.ReceiveEndpoint("chat.conversation-joined", endpoint =>
                {
                    ConfigureEndpoint(endpoint);
                    endpoint.ConfigureConsumer<ConversationJoinedConsumer>(context);
                });

                cfg.ReceiveEndpoint("chat.conversation-left", endpoint =>
                {
                    ConfigureEndpoint(endpoint);
                    endpoint.ConfigureConsumer<ConversationLeftConsumer>(context);
                });

                cfg.ReceiveEndpoint(MessagingConstants.MessageProjectionQueue, endpoint =>
                {
                    ConfigureEndpoint(endpoint);
                    endpoint.ConfigureConsumer<MessageProjectionRequestedConsumer>(context);
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