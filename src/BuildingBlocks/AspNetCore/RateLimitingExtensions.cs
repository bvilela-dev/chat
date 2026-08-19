using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.AspNetCore;

/// <summary>
/// Limitação de taxa para os endpoints de autenticação.
/// </summary>
/// <remarks>
/// <para>
/// <b>O problema.</b> <c>POST /api/auth/login</c> não tinha nenhum limite. Com
/// BCrypt levando ~100 ms por verificação, isso abre dois ataques distintos:
/// </para>
/// <list type="number">
///   <item><description>
///   <b>Força bruta / credential stuffing.</b> Sem limite, um atacante testa
///   listas de senhas vazadas até acertar. Nenhuma política de senha resiste a
///   tentativas ilimitadas.
///   </description></item>
///   <item><description>
///   <b>Negação de serviço pelo próprio hash.</b> O custo do BCrypt é
///   proposital, e é assimétrico: a requisição é barata de enviar e cara de
///   processar. Algumas centenas de logins por segundo saturam a CPU do
///   Identity Service e derrubam também os usuários legítimos.
///   </description></item>
/// </list>
/// <para>
/// <b>Por que <i>fixed window</i> e não <i>token bucket</i>.</b> A janela fixa é
/// mais fácil de explicar ao usuário ("10 tentativas por minuto") e de auditar.
/// Ela tem o defeito conhecido do efeito de borda — é possível gastar 10
/// tentativas no fim de uma janela e mais 10 no início da seguinte, 20 em poucos
/// segundos. Para conter força bruta isso é irrelevante: o que importa é a taxa
/// sustentada, e ela continua limitada.
/// </para>
/// <para>
/// <b>Limitação conhecida desta implementação.</b> O limitador é <i>por
/// instância</i>, mantido em memória. Com N réplicas do serviço, o limite
/// efetivo é N × 10. Para valer no cluster inteiro, o contador precisa ser
/// compartilhado — na prática, um contador em Redis com <c>INCR</c> + <c>EXPIRE</c>,
/// ou a limitação delegada ao ingress/gateway. Fica documentado como próximo
/// passo em vez de escondido.
/// </para>
/// </remarks>
public static class RateLimitingExtensions
{
    /// <summary>Política aplicada aos endpoints de autenticação.</summary>
    public const string AuthenticationPolicy = "authentication";

    /// <summary>
    /// Registra o limitador de taxa usado pelos endpoints sensíveis.
    /// </summary>
    public static WebApplicationBuilder AddChatRateLimiting(this WebApplicationBuilder builder)
    {
        builder.Services.AddRateLimiter(options =>
        {
            // 429 é o código correto: comunica ao cliente que a requisição era
            // válida, mas foi enviada rápido demais. O padrão do .NET seria 503,
            // que sugere indisponibilidade do servidor e induz o cliente a
            // repetir agressivamente.
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = async (context, cancellationToken) =>
            {
                // Informa em quanto tempo vale a pena tentar de novo. Um cliente
                // bem-comportado respeita o Retry-After em vez de ficar em loop.
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(NumberFormatInfo.InvariantInfo);
                }

                context.HttpContext.Response.ContentType = "application/problem+json";
                await context.HttpContext.Response.WriteAsync(
                    """
                    {"title":"Muitas tentativas. Aguarde alguns instantes e tente novamente.","status":429,"errorCode":"rate_limited"}
                    """,
                    cancellationToken);
            };

            options.AddPolicy(AuthenticationPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    // Particionar por IP, e não globalmente, é o que impede que um
                    // atacante em massa bloqueie o login de todos os usuários — o
                    // que transformaria a proteção contra DoS na própria DoS.
                    partitionKey: ResolveClientKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),

                        // Fila zero: excedeu, rejeita na hora. Enfileirar tentativas
                        // de login só serviria para segurar recursos do servidor em
                        // benefício de quem está abusando.
                        QueueLimit = 0
                    }));
        });

        return builder;
    }

    /// <summary>
    /// Deriva a chave de particionamento do chamador.
    /// </summary>
    /// <remarks>
    /// Atrás do Nginx e do gateway, <c>RemoteIpAddress</c> é o IP do proxy, e não
    /// o do usuário — o que colocaria todo mundo na mesma partição. A leitura
    /// correta depende do middleware de <i>forwarded headers</i> estar ativo e
    /// configurado com os proxies confiáveis; caso contrário,
    /// <c>X-Forwarded-For</c> é um cabeçalho que o cliente controla e pode
    /// falsificar à vontade para escapar do limite.
    /// </remarks>
    private static string ResolveClientKey(HttpContext httpContext)
    {
        return httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
