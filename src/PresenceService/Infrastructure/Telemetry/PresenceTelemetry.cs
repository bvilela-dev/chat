using System.Diagnostics.Metrics;
using PresenceService.Application.Abstractions;

namespace PresenceService.Infrastructure.Telemetry;

/// <summary>Métricas do Presence Service.</summary>
public sealed class PresenceTelemetry : IPresenceTelemetry
{
    /// <summary>Nome do meter, referenciado na configuração do OpenTelemetry.</summary>
    public const string MeterName = "PresenceService";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> CommandCounter =
        Meter.CreateCounter<long>(
            "presence.commands.total",
            unit: "{command}",
            description: "Total de comandos de presença processados.");

    /// <inheritdoc />
    public void RecordCommand(string commandName)
    {
        CommandCounter.Add(1, new KeyValuePair<string, object?>("command", commandName));
    }
}
