using System.Diagnostics.Metrics;
using NotificationService.Application.Abstractions;

namespace NotificationService.Infrastructure.Telemetry;

/// <summary>Métricas do Notification Service.</summary>
public sealed class NotificationTelemetry : INotificationTelemetry
{
    /// <summary>Nome do meter, referenciado na configuração do OpenTelemetry.</summary>
    public const string MeterName = "NotificationService";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> EventsCounter =
        Meter.CreateCounter<long>(
            "notification.events.total",
            unit: "{event}",
            description: "Eventos de integração consumidos pelo Notification Service.");

    private static readonly Counter<long> NotificationsCounter =
        Meter.CreateCounter<long>(
            "notification.sent.total",
            unit: "{notification}",
            description: "Notificações despachadas, por canal.");

    /// <inheritdoc />
    public void RecordEvent(string eventName)
    {
        EventsCounter.Add(1, new KeyValuePair<string, object?>("event", eventName));
    }

    /// <inheritdoc />
    public void RecordNotificationSent(string channel)
    {
        NotificationsCounter.Add(1, new KeyValuePair<string, object?>("channel", channel));
    }
}
