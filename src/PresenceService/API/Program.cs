using BuildingBlocks.Application;
using BuildingBlocks.AspNetCore;
using OpenTelemetry.Metrics;
using PresenceService.Application.Presence;
using PresenceService.Infrastructure;
using PresenceService.Infrastructure.Telemetry;

// =============================================================================
// PRESENCE SERVICE — quem está online agora.
//
// Todo o estado vive em Redis com expiração automática: presença é dado
// efêmero, de alta rotatividade, cuja perda total num incidente é irrelevante.
// Publica eventos de entrada e saída para que outros serviços reajam — o
// Notification Service, por exemplo, só notifica quem está offline.
// =============================================================================

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplicationBuildingBlocks(typeof(SetUserOnlineCommand).Assembly);
builder.Services.AddPresenceInfrastructure(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

builder.AddChatJwtAuthentication();
builder.AddChatObservability(serviceName: "presence-service", PresenceTelemetry.MeterName);
builder.AddChatForwardedHeaders();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseChatExceptionHandling();
app.UseChatForwardedHeaders();
app.UseAuthentication();
app.UseAuthorization();

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
