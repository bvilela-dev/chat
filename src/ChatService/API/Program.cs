using BuildingBlocks.Application;
using BuildingBlocks.AspNetCore;
using ChatService.API.Hubs;
using ChatService.API.Services;
using ChatService.Application.Abstractions;
using ChatService.Application.Messages;
using ChatService.Infrastructure;
using ChatService.Infrastructure.Telemetry;
using OpenTelemetry.Metrics;

// =============================================================================
// CHAT SERVICE — camada de tempo real.
//
// É o único serviço SEM BANCO DE DADOS, e isso é intencional. Ele mantém
// conexões WebSocket, roteia mensagens entre elas e publica eventos; a
// durabilidade é responsabilidade do Message Service.
//
// A consequência prática é que ele pode ser reiniciado ou escalado livremente:
// não há estado a migrar, os clientes reconectam sozinhos e nenhuma mensagem se
// perde — ela já foi publicada no RabbitMQ antes de ser transmitida.
// =============================================================================

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplicationBuildingBlocks(typeof(SendMessageCommand).Assembly);
builder.Services.AddChatInfrastructure(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// -----------------------------------------------------------------------------
// SignalR com backplane Redis.
//
// SEM O BACKPLANE, ESCALAR PARA MAIS DE UMA RÉPLICA QUEBRA O CHAT. Cada
// instância só conhece as próprias conexões, então uma mensagem transmitida pela
// réplica 1 jamais chegaria a um usuário conectado à réplica 2 — e o sintoma
// seria intermitente, dependendo de em qual pod cada usuário caiu.
//
// Com o backplane, a transmissão vai para um canal Redis e todas as réplicas a
// repassam às suas conexões locais.
// -----------------------------------------------------------------------------
builder.Services
    .AddSignalR(options =>
    {
        // Em desenvolvimento, envia o detalhe da exceção ao cliente. Jamais em
        // produção: exporia stack traces internos ao navegador.
        options.EnableDetailedErrors = builder.Environment.IsDevelopment();

        // Keep-alive e timeout ajustados para detectar conexão morta mais rápido
        // que o padrão, liberando os recursos do servidor sem esperar minutos.
        options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
    })
    .AddStackExchangeRedis(builder.Configuration.GetConnectionString("Redis") ?? "redis:6379");

builder.Services.AddScoped<IConversationNotifier, SignalRConversationNotifier>();

// `enableSignalRQueryStringToken: true` porque a API de WebSocket do navegador
// não permite enviar o cabeçalho Authorization no handshake. O encaminhamento é
// restrito ao caminho do hub.
builder.AddChatJwtAuthentication(enableSignalRQueryStringToken: true);

builder.AddChatObservability(serviceName: "chat-service", ChatTelemetry.MeterName);
builder.AddChatForwardedHeaders();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseChatExceptionHandling();
app.UseChatForwardedHeaders();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");
app.MapHealthChecks("/health");
app.MapPrometheusScrapingEndpoint();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

await app.RunAsync();

/// <summary>Ponto de entrada, exposto para os testes de integração.</summary>
public partial class Program;
