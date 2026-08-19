using System.Reflection;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildingBlocks.Application;

/// <summary>
/// Registro dos blocos de aplicação compartilhados no contêiner de DI.
/// </summary>
/// <remarks>
/// Concentrar esse registro num único método de extensão elimina a chance de um
/// serviço subir sem o pipeline de validação — que foi exatamente o defeito
/// original: cada <c>Program.cs</c> montava sua própria configuração e todos
/// esqueceram do <see cref="ValidationBehavior{TRequest, TResponse}"/>.
/// </remarks>
public static class ApplicationBuildingBlocksExtensions
{
    /// <summary>
    /// Registra MediatR, FluentValidation, o pipeline de validação e o relógio
    /// do sistema para o assembly de aplicação informado.
    /// </summary>
    /// <param name="services">Coleção de serviços do host.</param>
    /// <param name="applicationAssembly">
    /// Assembly onde estão os handlers e os validadores do serviço — na prática,
    /// o projeto <c>*.Application</c>. Passamos um <see cref="Assembly"/> em vez
    /// de varrer todos os assemblies carregados para manter o escaneamento
    /// previsível e barato no startup.
    /// </param>
    public static IServiceCollection AddApplicationBuildingBlocks(
        this IServiceCollection services,
        Assembly applicationAssembly)
    {
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(applicationAssembly);

            // A ORDEM DE REGISTRO É A ORDEM DE EXECUÇÃO do pipeline.
            // A validação vem primeiro para que nenhum outro behavior (log,
            // transação, métrica) gaste trabalho com uma requisição malformada.
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        // `includeInternalTypes: true` permite marcar validadores como `internal`,
        // mantendo-os fora da superfície pública do assembly.
        services.AddValidatorsFromAssembly(applicationAssembly, includeInternalTypes: true);

        // TryAdd em vez de Add: se um teste (ou o próprio serviço) já registrou um
        // relógio fixo antes desta chamada, respeitamos a substituição em vez de
        // sobrescrevê-la com o relógio real.
        services.TryAddSingleton<IClock, SystemClock>();

        return services;
    }
}
