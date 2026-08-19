using BuildingBlocks.Messaging;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PresenceService.Application.Abstractions;
using PresenceService.Infrastructure.Store;
using PresenceService.Infrastructure.Telemetry;
using StackExchange.Redis;

namespace PresenceService.Infrastructure;

/// <summary>Composição das dependências de infraestrutura do Presence Service.</summary>
public static class DependencyInjection
{
    /// <summary>Registra Redis, telemetria e mensageria do serviço.</summary>
    public static IServiceCollection AddPresenceInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IPresenceTelemetry, PresenceTelemetry>();

        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(configuration.GetConnectionString("Redis") ?? "redis:6379");

            // Não falhar no startup se o Redis ainda não estiver pronto: no
            // Kubernetes a ordem de inicialização dos pods não é garantida.
            options.AbortOnConnectFail = false;
            options.ConnectRetry = 3;

            return ConnectionMultiplexer.Connect(options);
        });

        services.AddSingleton<IPresenceStore, RedisPresenceStore>();
        services.AddScoped<IPresenceEventPublisher, PresenceEventPublisher>();

        services.AddMassTransit(bus =>
        {
            bus.SetKebabCaseEndpointNameFormatter();

            bus.UsingRabbitMq((context, rabbit) =>
            {
                rabbit.ConfigureRabbitMqHost(configuration);
                rabbit.ConfigureResilience();
                rabbit.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}
