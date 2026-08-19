using BuildingBlocks.Contracts.Grpc;
using BuildingBlocks.Messaging;
using ChatService.Application.Abstractions;
using ChatService.Infrastructure.Access;
using ChatService.Infrastructure.Messaging;
using ChatService.Infrastructure.Realtime;
using ChatService.Infrastructure.Telemetry;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace ChatService.Infrastructure;

/// <summary>
/// Composição das dependências de infraestrutura do Chat Service.
/// </summary>
public static class DependencyInjection
{
    /// <summary>Registra Redis, gRPC, telemetria e mensageria do serviço.</summary>
    public static IServiceCollection AddChatInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        AddRedis(services, configuration);
        AddConversationAccess(services, configuration);

        services.AddSingleton<IChatTelemetry, ChatTelemetry>();
        services.AddScoped<IChatEventPublisher, ChatEventPublisher>();

        AddMessaging(services, configuration);

        return services;
    }

    private static void AddRedis(IServiceCollection services, IConfiguration configuration)
    {
        var redisConnectionString = configuration.GetConnectionString("Redis") ?? "redis:6379";

        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(redisConnectionString);

            // Não abortar quando o Redis está indisponível no startup.
            //
            // Com o padrão (`AbortOnConnectFail = true`), o serviço falha ao
            // subir se o Redis ainda não estiver pronto — cenário rotineiro no
            // Kubernetes, onde a ordem de inicialização dos pods não é garantida.
            // Com `false`, o multiplexer sobe e reconecta sozinho quando o Redis
            // ficar disponível.
            options.AbortOnConnectFail = false;

            options.ConnectRetry = 3;
            options.ConnectTimeout = 5_000;

            return ConnectionMultiplexer.Connect(options);
        });

        services.AddSingleton<IConnectionRegistry, RedisConnectionRegistry>();
    }

    private static void AddConversationAccess(IServiceCollection services, IConfiguration configuration)
    {
        // Cache com teto de tamanho: sem `SizeLimit`, o IMemoryCache cresce até a
        // pressão de memória do processo — que num contêiner com limite rígido
        // significa OOM kill, não coleta de lixo.
        services.AddMemoryCache(options => options.SizeLimit = 10_000);

        var messageServiceAddress =
            configuration["Grpc:MessageService"] ?? "http://message-service:8080";

        services
            .AddGrpcClient<ConversationAccessGrpc.ConversationAccessGrpcClient>(options =>
            {
                options.Address = new Uri(messageServiceAddress);
            })
            // A fábrica de clientes gRPC gerencia o HttpClient subjacente,
            // reciclando conexões periodicamente. Isso importa em Kubernetes: sem
            // reciclagem, o cliente fixa os endereços IP resolvidos na primeira
            // conexão e nunca enxerga novos pods do serviço de destino.
            .ConfigureChannel(channel =>
            {
                channel.HttpHandler = new SocketsHttpHandler
                {
                    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
                    KeepAlivePingDelay = TimeSpan.FromSeconds(60),
                    KeepAlivePingTimeout = TimeSpan.FromSeconds(30),

                    // Habilita múltiplas conexões HTTP/2 quando o limite de
                    // streams de uma delas é atingido — evita um gargalo
                    // silencioso sob carga alta.
                    EnableMultipleHttp2Connections = true
                };
            });

        services.AddSingleton<IConversationAccessPolicy, GrpcConversationAccessPolicy>();
    }

    private static void AddMessaging(IServiceCollection services, IConfiguration configuration)
    {
        services.AddMassTransit(bus =>
        {
            bus.SetKebabCaseEndpointNameFormatter();

            bus.UsingRabbitMq((context, rabbit) =>
            {
                rabbit.ConfigureRabbitMqHost(configuration);
                rabbit.ConfigureResilience();

                // Este serviço só publica; não declara endpoints de consumo.
                rabbit.ConfigureEndpoints(context);
            });
        });
    }
}
