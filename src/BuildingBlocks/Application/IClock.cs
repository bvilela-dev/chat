namespace BuildingBlocks.Application;

/// <summary>
/// Abstração sobre o relógio do sistema.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que não usar <c>DateTime.UtcNow</c> direto?</b> Porque ele é uma
/// dependência global e não determinística: um handler que chama
/// <c>DateTime.UtcNow</c> não tem como ser testado de forma confiável — não dá
/// para verificar "o refresh token expira exatamente 7 dias depois" sem esperar
/// 7 dias, nem para testar o comportamento na virada do horário de verão.
/// </para>
/// <para>
/// Injetando <see cref="IClock"/>, o teste substitui a implementação por um
/// relógio fixo (<c>FixedClock</c>) e passa a controlar o tempo. É a mesma ideia
/// do <c>TimeProvider</c> introduzido no .NET 8; mantemos a interface própria
/// por ser mais enxuta e por já estar estabelecida nos handlers.
/// </para>
/// <para>
/// Convenção do projeto: <b>todo</b> instante é UTC. Datas locais nunca cruzam a
/// fronteira de um serviço — a conversão para o fuso do usuário é
/// responsabilidade exclusiva do frontend.
/// </para>
/// </remarks>
public interface IClock
{
    /// <summary>Instante atual em UTC.</summary>
    DateTime UtcNow { get; }
}

/// <summary>
/// Implementação de produção do <see cref="IClock"/>, ancorada no relógio da máquina.
/// </summary>
/// <remarks>
/// Registrada como <i>singleton</i>: não guarda estado e criar uma instância por
/// requisição seria desperdício.
/// </remarks>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTime UtcNow => DateTime.UtcNow;
}
