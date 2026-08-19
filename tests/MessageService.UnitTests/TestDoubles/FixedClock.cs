using BuildingBlocks.Application;

namespace MessageService.UnitTests.TestDoubles;

/// <summary>Relógio determinístico para os testes deste serviço.</summary>
public sealed class FixedClock(DateTime utcNow) : IClock
{
    /// <summary>Instante de referência padrão.</summary>
    public static readonly DateTime DefaultInstant = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    /// <inheritdoc />
    public DateTime UtcNow { get; } = utcNow;

    /// <summary>Cria um relógio no instante padrão.</summary>
    public static FixedClock Default() => new(DefaultInstant);
}
