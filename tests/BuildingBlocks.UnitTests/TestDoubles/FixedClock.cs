using BuildingBlocks.Application;

namespace BuildingBlocks.UnitTests.TestDoubles;

/// <summary>
/// Relógio de teste que devolve sempre o mesmo instante, e que pode ser avançado
/// manualmente.
/// </summary>
/// <remarks>
/// <para>
/// É a razão de <see cref="IClock"/> existir. Com <c>DateTime.UtcNow</c>
/// embutido nos handlers, não haveria como escrever um teste como "o refresh
/// token expira exatamente 7 dias depois" — só restaria esperar sete dias, ou
/// não testar.
/// </para>
/// <para>
/// Com um relógio controlado, o tempo vira mais um parâmetro do teste. Isso
/// também elimina uma classe inteira de testes instáveis: os que falham uma vez
/// a cada mil execuções porque o relógio virou o segundo entre duas linhas.
/// </para>
/// </remarks>
public sealed class FixedClock(DateTime utcNow) : IClock
{
    /// <summary>Instante de referência padrão dos testes.</summary>
    /// <remarks>
    /// Uma data fixa e explícita, em UTC. Usar <c>DateTime.UtcNow</c> como base
    /// aqui reintroduziria exatamente o não determinismo que este dublê existe
    /// para eliminar.
    /// </remarks>
    public static readonly DateTime DefaultInstant = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    /// <inheritdoc />
    public DateTime UtcNow { get; private set; } = utcNow;

    /// <summary>Cria um relógio ancorado no instante padrão.</summary>
    public static FixedClock Default() => new(DefaultInstant);

    /// <summary>Avança o relógio, para exercitar expirações e janelas de tempo.</summary>
    public void Advance(TimeSpan amount) => UtcNow = UtcNow.Add(amount);
}
