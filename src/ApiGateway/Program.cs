using BuildingBlocks.AspNetCore;
using OpenTelemetry.Metrics;

// =============================================================================
// API GATEWAY — ponto único de entrada da plataforma.
//
// Implementado com YARP (Yet Another Reverse Proxy), a biblioteca de proxy
// reverso da Microsoft.
//
// POR QUE EXISTE UM GATEWAY
// -------------------------
// Sem ele, o frontend precisaria conhecer o endereço de cinco serviços, e cada
// um deles teria de resolver CORS, TLS e limitação de taxa por conta própria. Com
// o gateway, o cliente enxerga uma única origem e a política transversal fica
// num lugar só.
//
// O QUE ELE DELIBERADAMENTE NÃO FAZ
// ---------------------------------
// Ele NÃO é o único ponto de verificação de autorização. Cada serviço valida o
// JWT por conta própria e aplica suas próprias regras. É o princípio de "zero
// trust" aplicado internamente: um atacante que alcance a rede do cluster e
// converse direto com o Message Service, contornando o gateway, ainda esbarra em
// autenticação e autorização completas.
//
// Tratar o gateway como a única fronteira de segurança é o erro clássico do
// modelo "casca dura, miolo mole".
// =============================================================================

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// A tabela de rotas e destinos vem da configuração (appsettings + variáveis de
// ambiente), e não do código. Isso permite reapontar um destino sem recompilar.
builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// O gateway valida o token para poder rejeitar requisições obviamente inválidas
// cedo, poupando os serviços internos. A validação definitiva continua sendo
// feita por cada serviço.
builder.AddChatJwtAuthentication();

// CORS com lista de origens permitidas — substitui o "aceita qualquer origem com
// credenciais" da versão anterior. Ver CorsExtensions para o detalhe do risco.
builder.AddChatCors();

builder.AddChatObservability(serviceName: "api-gateway");
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseChatExceptionHandling();

// CORS antes de autenticação: a requisição de preflight (OPTIONS) é enviada pelo
// navegador SEM cabeçalho Authorization, por definição. Se a autenticação viesse
// primeiro, todo preflight tomaria 401 e nenhuma chamada cross-origin
// funcionaria — um sintoma que costuma ser diagnosticado erroneamente como
// "problema de CORS" quando na verdade é ordem de middleware.
app.UseCors(CorsExtensions.PolicyName);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapReverseProxy();
app.MapHealthChecks("/health");
app.MapPrometheusScrapingEndpoint();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

await app.RunAsync();

/// <summary>Ponto de entrada, exposto para os testes de integração.</summary>
public partial class Program;
