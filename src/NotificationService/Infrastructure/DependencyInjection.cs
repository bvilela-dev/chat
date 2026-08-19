using BuildingBlocks.Contracts;
using BuildingBlocks.Messaging;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NotificationService.Application.Abstractions;
using NotificationService.Infrastructure.Messaging;
using NotificationService.Infrastructure.Store;
using NotificationService.Infrastructure.Telemetry;
using StackExchange.Redis;

namespace NotificationService.Infrastructure;

/// <summary>Composição das dependências de infraestrutura do Notification Service.</summary>
public static class DependencyInjection
{
    /// <summary>Registra Redis, telemetria e os consumidores de eventos.</summary>
    public static IServiceCollection AddNotificationInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<INotificationTelemetry, NotificationTelemetry>();

        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(configuration.GetConnectionString("Redis") ?? "redis:6379");
            options.AbortOnConnectFail = false;
            options.ConnectRetry = 3;
            return ConnectionMultiplexer.Connect(options);
        });

        services.AddSingleton<IConversationMembershipStore, RedisConversationMembershipStore>();
        services.AddSingleton<IPresenceLookup, RedisPresenceLookup>();
        services.AddSingleton<INotificationSender, LoggingNotificationSender>();

        services.AddMassTransit(bus =>
        {
            bus.AddConsumer<MessageSentConsumer>();
            bus.AddConsumer<ConversationJoinedConsumer>();
            bus.AddConsumer<ConversationLeftConsumer>();
            bus.SetKebabCaseEndpointNameFormatter();

            bus.UsingRabbitMq((context, rabbit) =>
            {
                rabbit.ConfigureRabbitMqHost(configuration);

                // Filas próprias, distintas das do Message Service.
                //
                // É a essência do publish/subscribe: o mesmo MessageSentEvent é
                // entregue às duas filas, e cada serviço o processa no seu ritmo,
                // com o próprio controle de falha. Se o Notification Service
                // ficar fora do ar, a fila dele acumula mensagens sem afetar em
                // nada a persistência feita pelo Message Service.
                rabbit.ReceiveEndpoint(MessagingConstants.NotificationQueue, endpoint =>
                {
                    endpoint.ConfigureResilience();
                    endpoint.ConfigureConsumer<MessageSentConsumer>(context);
                });

                rabbit.ReceiveEndpoint(MessagingConstants.NotificationConversationJoinedQueue, endpoint =>
                {
                    endpoint.ConfigureResilience();
                    endpoint.ConfigureConsumer<ConversationJoinedConsumer>(context);
                });

                rabbit.ReceiveEndpoint(MessagingConstants.NotificationConversationLeftQueue, endpoint =>
                {
                    endpoint.ConfigureResilience();
                    endpoint.ConfigureConsumer<ConversationLeftConsumer>(context);
                });
            });
        });

        return services;
    }
}
