using BuildingBlocks.Application;
using BuildingBlocks.AspNetCore;
using MessageService.API.Grpc;
using MessageService.Application.Messages;
using MessageService.Infrastructure;
using MessageService.Infrastructure.Persistence;
using MessageService.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;

// =============================================================================
// MESSAGE SERVICE — persistência, histórico e autorização de conversas.
//
// É o lado "durável" do chat. Enquanto o Chat Service entrega mensagens em tempo
// real (rápido e volátil), este serviço as grava, mantém as projeções de leitura
// e é a fonte de verdade sobre quem participa de qual conversa.
//
// Expõe três superfícies distintas:
//   - REST  → consumida pelo frontend (histórico e lista de conversas)
//   - gRPC  → consumida pelo Chat Service (checagem de participação)
//   - AMQP  → consumidores de eventos do RabbitMQ (persistência e projeções)
// =============================================================================

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplicationBuildingBlocks(typeof(GetMessagesByConversationQuery).Assembly);
builder.Services.AddMessageInfrastructure(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
// Kestrel precisa aceitar HTTP/1.1 (REST) e HTTP/2 (gRPC) na mesma porta.
// Sem isto, o Chat Service não consegue consultar a participação em conversas
// e a política de acesso — que falha fechada — nega TODOS os acessos,
// inclusive os legítimos.
builder.AddChatGrpcAndRestHosting(
    // Portas configuráveis para que os serviços possam rodar lado a lado na mesma
    // máquina, fora de contêineres, sem colidir. Nos contêineres cada serviço tem
    // o próprio espaço de portas e os padrões bastam.
    restPort: builder.Configuration.GetValue("Ports:Rest", GrpcHostingExtensions.DefaultRestPort),
    grpcPort: builder.Configuration.GetValue("Ports:Grpc", GrpcHostingExtensions.DefaultGrpcPort));

builder.Services.AddGrpc();

builder.AddChatJwtAuthentication();
builder.AddChatObservability(serviceName: "message-service", MessageTelemetry.MeterName);

builder.AddChatForwardedHeaders();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<MessageDbContext>(name: "message-database");

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<MessageDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.UseChatExceptionHandling();
app.UseChatForwardedHeaders();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGrpcService<ConversationAccessGrpcService>();
app.MapHealthChecks("/health");
app.MapPrometheusScrapingEndpoint();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

await app.RunAsync();

/// <summary>Ponto de entrada, exposto para os testes de integração.</summary>
public partial class Program;
