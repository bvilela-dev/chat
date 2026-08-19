using System.Diagnostics.Metrics;
using MessageService.Application.Abstractions;

namespace MessageService.Infrastructure.Telemetry;

/// <summary>
/// Métricas do Message Service, expostas via OpenTelemetry.
/// </summary>
/// <remarks>
/// O nome do meter (<c>MessageService</c>) precisa bater exatamente com o que é
/// registrado no <c>AddChatObservability</c>. Divergência aqui produz uma falha
/// silenciosa clássica: a aplicação funciona, as métricas são coletadas
/// internamente e simplesmente nunca aparecem no Prometheus.
/// </remarks>
public sealed class MessageTelemetry : IMessageTelemetry
{
    /// <summary>Nome do meter, referenciado na configuração do OpenTelemetry.</summary>
    public const string MeterName = "MessageService";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> EventsCounter =
        Meter.CreateCounter<long>(
            "message.events.total",
            unit: "{event}",
            description: "Total de eventos de integração consumidos pelo Message Service.");

    private static readonly Histogram<double> ProjectionLagMilliseconds =
        Meter.CreateHistogram<double>(
            "message.projection.lag",
            unit: "ms",
            description: "Atraso entre o envio da mensagem e a atualização do read model.");

    /// <inheritdoc />
    public void RecordConsumedEvent(string eventName)
    {
        // A tag permite fatiar o contador por tipo de evento no painel. Cuidado
        // conhecido: valores de tag devem vir de um conjunto FECHADO (nomes de
        // eventos, aqui). Usar um identificador de usuário como tag geraria uma
        // série temporal nova por usuário — o problema de "explosão de
        // cardinalidade" que derruba instâncias de Prometheus.
        EventsCounter.Add(1, new KeyValuePair<string, object?>("event", eventName));
    }

    /// <inheritdoc />
    public void RecordProjectionLag(TimeSpan lag)
    {
        // Histograma, e não gauge: o interessante é a DISTRIBUIÇÃO. A média
        // esconde justamente o que importa — um p99 de 5 segundos afeta 1% dos
        // usuários e é invisível numa média de 50 ms.
        ProjectionLagMilliseconds.Record(lag.TotalMilliseconds);
    }
}
