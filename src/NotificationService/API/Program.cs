using BuildingBlocks.Application;
using BuildingBlocks.AspNetCore;
using NotificationService.Application.Notifications;
using NotificationService.Infrastructure;
using NotificationService.Infrastructure.Telemetry;
using OpenTelemetry.Metrics;

// =============================================================================
// NOTIFICATION SERVICE — avisa quem não está com o chat aberto.
//
// É um serviço praticamente SEM API: sua porta de entrada real são as filas do
// RabbitMQ. O único endpoint HTTP existe para health check e coleta de métricas
// — algo que o Kubernetes e o Prometheus exigem.
//
// É um bom exemplo de que "microsserviço" não é sinônimo de "API REST". Este
// aqui é um processador de eventos, e a arquitetura orientada a mensagens é o
// que permite acrescentá-lo (ou removê-lo) sem que nenhum outro serviço saiba.
// =============================================================================

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplicationBuildingBlocks(typeof(NotifyOfflineUsersCommand).Assembly);
builder.Services.AddNotificationInfrastructure(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

builder.AddChatObservability(serviceName: "notification-service", NotificationTelemetry.MeterName);
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseChatExceptionHandling();

app.MapControllers();
app.MapHealthChecks("/health");
app.MapPrometheusScrapingEndpoint();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

await app.RunAsync();

/// <summary>Ponto de entrada, exposto para os testes de integração.</summary>
public partial class Program;
