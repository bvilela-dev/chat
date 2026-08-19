using BuildingBlocks.Application;

namespace IdentityService.UnitTests.TestDoubles;

/// <summary>Relógio determinístico para os testes deste serviço.</summary>
public sealed class FixedClock(DateTime utcNow) : IClock
{
    /// <summary>Instante de referência padrão.</summary>
    public static readonly DateTime DefaultInstant = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    /// <inheritdoc />
    public DateTime UtcNow { get; private set; } = utcNow;

    /// <summary>Cria um relógio no instante padrão.</summary>
    public static FixedClock Default() => new(DefaultInstant);

    /// <summary>Avança o relógio para exercitar expirações.</summary>
    public void Advance(TimeSpan amount) => UtcNow = UtcNow.Add(amount);
}
