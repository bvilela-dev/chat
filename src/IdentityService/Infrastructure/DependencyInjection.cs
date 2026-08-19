using BuildingBlocks.Messaging;
using IdentityService.Application.Abstractions;
using IdentityService.Infrastructure.Messaging;
using IdentityService.Infrastructure.Persistence;
using IdentityService.Infrastructure.Security;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityService.Infrastructure;

/// <summary>
/// Composição das dependências de infraestrutura do Identity Service.
/// </summary>
/// <remarks>
/// Este é o <i>composition root</i> da camada: o único lugar onde as abstrações
/// declaradas na camada de Aplicação são ligadas às suas implementações
/// concretas. Concentrar isso aqui é o que permite ao <c>Program.cs</c> ser
/// curto e legível.
/// </remarks>
public static class DependencyInjection
{
    /// <summary>Registra persistência, segurança e mensageria do serviço.</summary>
    public static IServiceCollection AddIdentityInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        AddPersistence(services, configuration);
        AddSecurity(services);
        AddMessaging(services, configuration);

        return services;
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<IdentityDbContext>(options => options
            .UseNpgsql(
                configuration.GetConnectionString("IdentityDatabase"),
                npgsql => npgsql
                    // Resiliência de conexão: reexecuta automaticamente comandos
                    // que falharam por erro transitório (reinício do banco,
                    // oscilação de rede, failover). Sem isso, qualquer soluço da
                    // rede vira um 500 para o usuário.
                    //
                    // Atenção: com estratégia de retry habilitada, transações
                    // controladas manualmente precisam ser envolvidas em
                    // `CreateExecutionStrategy().ExecuteAsync(...)` — do
                    // contrário o EF lança uma exceção explicando isso. Aqui
                    // usamos apenas o SaveChanges transacional implícito, então
                    // não há conflito.
                    .EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(5),
                        errorCodesToAdd: null)));

        services.AddScoped<IUserRepository, UserRepository>();

        // Scoped, e não Singleton: precisa compartilhar a MESMA instância de
        // DbContext do repositório dentro da requisição, senão a linha da outbox
        // cairia numa transação separada e o padrão perderia a atomicidade.
        services.AddScoped<IOutboxWriter, EfOutboxWriter>();
    }

    private static void AddSecurity(IServiceCollection services)
    {
        // Singletons: ambos são sem estado e caros de construir (o
        // BcryptPasswordHasher calcula o hash descartável na carga do tipo).
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
    }

    private static void AddMessaging(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHostedService<IdentityOutboxDispatcher>();

        services.AddMassTransit(bus =>
        {
            // Padroniza os nomes de fila/exchange em kebab-case
            // (`user-created-event`), evitando divergência de nomenclatura entre
            // os serviços — que se manifestaria como mensagens publicadas numa
            // exchange que ninguém escuta.
            bus.SetKebabCaseEndpointNameFormatter();

            bus.UsingRabbitMq((_, rabbit) =>
            {
                rabbit.ConfigureRabbitMqHost(configuration);
                rabbit.ConfigureResilience();

                // Este serviço apenas publica; não declara consumidores.
                rabbit.ConfigureEndpoints(_);
            });
        });
    }
}
