using BuildingBlocks.Application;
using BuildingBlocks.AspNetCore;
using IdentityService.API.Grpc;
using IdentityService.Application.Users;
using IdentityService.Infrastructure;
using IdentityService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;

// =============================================================================
// IDENTITY SERVICE — cadastro, autenticação e diretório de usuários.
//
// É a única fonte de verdade sobre identidade na plataforma. Ele ASSINA os
// tokens JWT; os demais serviços apenas os VERIFICAM. Nenhum outro serviço
// acessa o banco `chat_identity`: quem precisa saber que um usuário foi criado
// recebe o evento `UserCreatedEvent` pelo RabbitMQ.
// =============================================================================

var builder = WebApplication.CreateBuilder(args);

// -----------------------------------------------------------------------------
// Camada de aplicação: MediatR + FluentValidation + pipeline de validação.
// Passar o assembly de um comando conhecido é uma forma segura de apontar para o
// projeto *.Application sem depender de uma string com o nome do assembly.
// -----------------------------------------------------------------------------
builder.Services.AddApplicationBuildingBlocks(typeof(RegisterUserCommand).Assembly);

builder.Services.AddIdentityInfrastructure(builder.Configuration);

// -----------------------------------------------------------------------------
// Borda HTTP.
// -----------------------------------------------------------------------------
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// gRPC serve à comunicação interna entre serviços (o Chat Service valida
// usuários por aqui). É preferível a REST nesse caso por ser um contrato
// fortemente tipado, com serialização binária e latência menor — e por não ser
// exposto ao mundo externo.
// Kestrel precisa aceitar HTTP/1.1 (REST) e HTTP/2 (gRPC) na mesma porta.
builder.AddChatGrpcAndRestHosting(
    // Portas configuráveis para que os serviços possam rodar lado a lado na mesma
    // máquina, fora de contêineres, sem colidir. Nos contêineres cada serviço tem
    // o próprio espaço de portas e os padrões bastam.
    restPort: builder.Configuration.GetValue("Ports:Rest", GrpcHostingExtensions.DefaultRestPort),
    grpcPort: builder.Configuration.GetValue("Ports:Grpc", GrpcHostingExtensions.DefaultGrpcPort));

builder.Services.AddGrpc();

// Registra a autenticação JWT com verificação de configuração segura: em
// produção, a chave de desenvolvimento faz a aplicação falhar no startup.
builder.AddChatJwtAuthentication();

// Protege os endpoints de autenticação contra força bruta.
builder.AddChatRateLimiting();

// Restaura o IP real do cliente a partir de X-Forwarded-For.
//
// Sem isto, o rate limiting particiona pelo IP do API Gateway — ou seja, TODOS
// os usuários compartilham a mesma cota, e um único atacante bloqueia o login de
// toda a base.
builder.AddChatForwardedHeaders();

builder.AddChatObservability(serviceName: "identity-service");

builder.Services.AddHealthChecks()
    // O health check checa o banco: um serviço que não alcança o PostgreSQL não
    // consegue autenticar ninguém e não deve receber tráfego. Sem esta
    // verificação, o `/health` responderia "saudável" com o banco fora do ar, e
    // o Kubernetes manteria a réplica no balanceador retornando 500.
    .AddDbContextCheck<IdentityDbContext>(name: "identity-database");

var app = builder.Build();

// -----------------------------------------------------------------------------
// Migrations automáticas no startup.
//
// Prático para demonstração e ambiente local, mas vale registrar a ressalva:
// em produção com múltiplas réplicas, todas tentam migrar ao mesmo tempo. O EF
// Core usa um lock consultivo no PostgreSQL, então não corrompe — porém o padrão
// recomendado é rodar as migrations como um passo separado do deploy (um Job do
// Kubernetes, ou um init container), para que a mudança de schema seja explícita
// e reversível, e não um efeito colateral de subir a aplicação.
// -----------------------------------------------------------------------------
await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    await dbContext.Database.MigrateAsync();
}

// -----------------------------------------------------------------------------
// Pipeline HTTP. A ORDEM IMPORTA e é a fonte mais comum de bugs sutis aqui.
// -----------------------------------------------------------------------------

// Primeiro de todos: só captura exceções do que vem depois dele.
app.UseChatExceptionHandling();

// ANTES do rate limiter: é aqui que o IP real do cliente é restaurado. Invertida
// a ordem, o limitador ainda enxergaria o IP do gateway.
app.UseChatForwardedHeaders();

app.UseRateLimiter();

// Autenticação ("quem é você?") sempre antes de autorização ("você pode?").
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGrpcService<UserValidationGrpcService>();
app.MapHealthChecks("/health");
app.MapPrometheusScrapingEndpoint();

// A documentação OpenAPI descreve a superfície interna da API; publicá-la fora
// de desenvolvimento entrega de graça o mapa de endpoints a quem for sondar.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

await app.RunAsync();

/// <summary>
/// Declaração explícita da classe de entrada gerada pelos top-level statements.
/// </summary>
/// <remarks>
/// Necessária para que os testes de integração possam referenciar
/// <c>WebApplicationFactory&lt;Program&gt;</c>. Sem isto, a classe gerada pelo
/// compilador é <c>internal</c> e inacessível ao projeto de testes.
/// </remarks>
public partial class Program;
