using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace BuildingBlocks.AspNetCore;

/// <summary>
/// Configuração única de autenticação JWT para todos os serviços da plataforma.
/// </summary>
public static class JwtAuthenticationExtensions
{
    /// <summary>
    /// Segmento de rota do hub SignalR. O handshake WebSocket não permite enviar
    /// cabeçalho <c>Authorization</c>, então esse caminho recebe um tratamento
    /// especial (ver <see cref="ConfigureSignalRTokenForwarding"/>).
    /// </summary>
    private const string ChatHubPath = "/hubs/chat";

    /// <summary>
    /// Lê a seção <c>Jwt</c>, valida-a e registra a autenticação por Bearer token.
    /// </summary>
    /// <param name="builder">Builder da aplicação web.</param>
    /// <param name="enableSignalRQueryStringToken">
    /// Habilita a leitura do token pela query string no caminho do hub. Deve ser
    /// <c>true</c> apenas no Chat Service.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Lançada no startup quando a configuração é insegura fora de Development.
    /// </exception>
    public static WebApplicationBuilder AddChatJwtAuthentication(
        this WebApplicationBuilder builder,
        bool enableSignalRQueryStringToken = false)
    {
        var jwtOptions = builder.Configuration
            .GetSection(JwtOptions.SectionName)
            .Get<JwtOptions>() ?? new JwtOptions();

        GuardAgainstInsecureConfiguration(jwtOptions, builder.Environment);

        // Disponibiliza as opções para quem precisar emitir tokens (Identity Service).
        builder.Services.AddSingleton(jwtOptions);

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    // Cada uma destas verificações fecha um vetor de ataque concreto:

                    // Sem isto, um token assinado com a mesma chave por OUTRO
                    // sistema da empresa seria aceito aqui.
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,

                    // Sem isto, um token emitido para outro público (ex.: um app
                    // parceiro com escopo reduzido) seria aceito nesta API.
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,

                    // Sem isto, um token vazado valeria para sempre.
                    ValidateLifetime = true,

                    // Sem isto, qualquer um forjaria tokens — é a verificação
                    // criptográfica da assinatura.
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),

                    // O padrão do .NET é tolerar 5 minutos de diferença de relógio
                    // entre emissor e validador. Isso estende na prática um token
                    // de 15 minutos para 20. Com os serviços sincronizados por
                    // NTP, 30 segundos são suficientes e a janela de exposição de
                    // um token expirado cai para quase nada.
                    ClockSkew = TimeSpan.FromSeconds(30)
                };

                if (enableSignalRQueryStringToken)
                {
                    ConfigureSignalRTokenForwarding(options);
                }
            });

        builder.Services.AddAuthorization();

        return builder;
    }

    /// <summary>
    /// Impede que o serviço suba com uma configuração de assinatura insegura.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Esta é a correção mais importante deste arquivo. O código anterior fazia:
    /// </para>
    /// <code>
    /// var jwtKey = builder.Configuration["Jwt:Key"] ?? "super-secret-development-key-change-me";
    /// </code>
    /// <para>
    /// O operador <c>??</c> parece defensivo, mas cria uma falha silenciosa: se a
    /// variável de ambiente não fosse injetada em produção — um typo no manifesto
    /// Kubernetes, um secret não montado — o serviço subiria <b>normalmente</b>,
    /// os health checks passariam, e a plataforma inteira estaria assinando
    /// tokens com um segredo público, versionado no Git. Qualquer pessoa com
    /// acesso ao repositório poderia forjar um token de administrador.
    /// </para>
    /// <para>
    /// A postura correta é <i>fail fast</i>: em produção, uma configuração de
    /// segurança ausente é um erro fatal de inicialização, não um valor padrão. É
    /// melhor o deploy falhar de forma barulhenta do que rodar inseguro em
    /// silêncio.
    /// </para>
    /// </remarks>
    private static void GuardAgainstInsecureConfiguration(JwtOptions options, IHostEnvironment environment)
    {
        // Em desenvolvimento o padrão inseguro é aceito de propósito: baixa a
        // barreira para clonar o repositório e rodar. O aviso deixa claro que
        // aquilo não é o comportamento de produção.
        if (environment.IsDevelopment())
        {
            if (options.UsesInsecureDevelopmentKey)
            {
                Console.WriteLine(
                    "[AVISO] Usando a chave JWT de desenvolvimento. Defina Jwt__Key antes de qualquer deploy.");
            }

            return;
        }

        if (options.UsesInsecureDevelopmentKey)
        {
            throw new InvalidOperationException(
                $"A chave JWT de desenvolvimento não pode ser usada no ambiente '{environment.EnvironmentName}'. " +
                "Defina a variável de ambiente 'Jwt__Key' com um segredo forte e exclusivo deste ambiente.");
        }

        if (string.IsNullOrWhiteSpace(options.Key) || options.Key.Length < 32)
        {
            throw new InvalidOperationException(
                "A chave JWT precisa de ao menos 32 caracteres para assinatura HMAC-SHA256 segura.");
        }
    }

    /// <summary>
    /// Permite que o SignalR autentique o handshake através da query string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A API de <c>WebSocket</c> do navegador não deixa definir cabeçalhos HTTP
    /// personalizados no handshake. Por isso o cliente SignalR envia o token como
    /// <c>?access_token=...</c>, e este evento o transfere para o pipeline de
    /// autenticação padrão.
    /// </para>
    /// <para>
    /// <b>Cuidado de segurança:</b> token em query string aparece em log de
    /// servidor, histórico de proxy e cabeçalho <c>Referer</c>. Por isso o
    /// encaminhamento é restrito ao caminho do hub — nenhum outro endpoint aceita
    /// credencial por URL. É também mais um motivo para o access token ter vida
    /// curta.
    /// </para>
    /// </remarks>
    private static void ConfigureSignalRTokenForwarding(JwtBearerOptions options)
    {
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];

                if (!string.IsNullOrWhiteSpace(accessToken) &&
                    context.HttpContext.Request.Path.StartsWithSegments(ChatHubPath))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    }
}
