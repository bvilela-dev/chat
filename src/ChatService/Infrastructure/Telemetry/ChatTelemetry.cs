using System.Diagnostics.Metrics;
using ChatService.Application.Abstractions;

namespace ChatService.Infrastructure.Telemetry;

/// <summary>Métricas do Chat Service, expostas via OpenTelemetry.</summary>
public sealed class ChatTelemetry : IChatTelemetry
{
    /// <summary>Nome do meter, referenciado na configuração do OpenTelemetry.</summary>
    public const string MeterName = "ChatService";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> CommandCounter =
        Meter.CreateCounter<long>(
            "chat.commands.total",
            unit: "{command}",
            description: "Total de comandos processados pelo Chat Service.");

    private static readonly Counter<long> AccessDeniedCounter =
        Meter.CreateCounter<long>(
            "chat.access_denied.total",
            unit: "{attempt}",
            description: "Tentativas de acesso a conversas negadas pela política de autorização.");

    /// <summary>
    /// Contador de conexões ativas.
    /// </summary>
    /// <remarks>
    /// <c>long</c> com <see cref="Interlocked"/>, e não um campo comum: o valor é
    /// alterado concorrentemente por milhares de conexões abrindo e fechando.
    /// Um <c>_contador++</c> simples perderia incrementos por condição de corrida
    /// e o número exibido no painel iria divergindo do real com o tempo.
    /// </remarks>
    private static long _activeConnections;

    // O gauge observável é consultado pelo OpenTelemetry no momento da coleta.
    // O campo precisa ser mantido para que o instrumento não seja recolhido pelo
    // coletor de lixo — sem a referência, a métrica simplesmente para de aparecer.
    private static readonly ObservableGauge<long> ConnectionsGauge =
        Meter.CreateObservableGauge(
            "chat.signalr.connections",
            () => Interlocked.Read(ref _activeConnections),
            unit: "{connection}",
            description: "Conexões SignalR ativas nesta instância.");

    /// <inheritdoc />
    public void IncrementCommand(string commandName)
    {
        CommandCounter.Add(1, new KeyValuePair<string, object?>("command", commandName));
    }

    /// <inheritdoc />
    public void ConnectionOpened() => Interlocked.Increment(ref _activeConnections);

    /// <inheritdoc />
    public void ConnectionClosed() => Interlocked.Decrement(ref _activeConnections);

    /// <inheritdoc />
    public void AccessDenied(string reason)
    {
        AccessDeniedCounter.Add(1, new KeyValuePair<string, object?>("reason", reason));
    }
}
