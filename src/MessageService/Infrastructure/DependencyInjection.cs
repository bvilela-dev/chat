using BuildingBlocks.Contracts;
using BuildingBlocks.Messaging;
using MassTransit;
using MessageService.Application.Abstractions;
using MessageService.Infrastructure.Messaging;
using MessageService.Infrastructure.Persistence;
using MessageService.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MessageService.Infrastructure;

/// <summary>
/// Composição das dependências de infraestrutura do Message Service.
/// </summary>
public static class DependencyInjection
{
    /// <summary>Registra persistência, telemetria e mensageria do serviço.</summary>
    public static IServiceCollection AddMessageInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<MessageDbContext>(options => options
            .UseNpgsql(
                configuration.GetConnectionString("MessageDatabase"),
                npgsql => npgsql.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorCodesToAdd: null)));

        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddSingleton<IMessageTelemetry, MessageTelemetry>();
        services.AddHostedService<MessageOutboxDispatcher>();

        AddMessaging(services, configuration);

        return services;
    }

    private static void AddMessaging(IServiceCollection services, IConfiguration configuration)
    {
        services.AddMassTransit(bus =>
        {
            bus.AddConsumer<MessageSentConsumer>();
            bus.AddConsumer<ConversationJoinedConsumer>();
            bus.AddConsumer<ConversationLeftConsumer>();
            bus.AddConsumer<MessageProjectionRequestedConsumer>();
            bus.SetKebabCaseEndpointNameFormatter();

            bus.UsingRabbitMq((context, rabbit) =>
            {
                rabbit.ConfigureRabbitMqHost(configuration);

                // FILAS NOMEADAS EXPLICITAMENTE, e não geradas por convenção.
                //
                // O nome da fila é infraestrutura durável: ele existe no
                // RabbitMQ, com mensagens dentro, independentemente do código.
                // Deixar que ele derive do nome da classe do consumidor cria uma
                // armadilha — renomear a classe numa refatoração faz o serviço
                // passar a escutar uma fila NOVA e VAZIA, enquanto a fila antiga
                // acumula mensagens que ninguém mais consome. O sintoma
                // ("mensagens sumiram") não aponta em nada para a causa.
                //
                // Com nomes constantes, refatorar código nunca mexe em topologia.
                rabbit.ReceiveEndpoint(MessagingConstants.ChatPersistQueue, endpoint =>
                {
                    endpoint.ConfigureResilience();
                    endpoint.ConfigureConsumer<MessageSentConsumer>(context);
                });

                rabbit.ReceiveEndpoint(MessagingConstants.MessageConversationJoinedQueue, endpoint =>
                {
                    endpoint.ConfigureResilience();
                    endpoint.ConfigureConsumer<ConversationJoinedConsumer>(context);
                });

                rabbit.ReceiveEndpoint(MessagingConstants.MessageConversationLeftQueue, endpoint =>
                {
                    endpoint.ConfigureResilience();
                    endpoint.ConfigureConsumer<ConversationLeftConsumer>(context);
                });

                rabbit.ReceiveEndpoint(MessagingConstants.MessageProjectionQueue, endpoint =>
                {
                    endpoint.ConfigureResilience();
                    endpoint.ConfigureConsumer<MessageProjectionRequestedConsumer>(context);
                });
            });
        });
    }
}
