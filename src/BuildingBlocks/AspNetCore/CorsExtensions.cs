using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BuildingBlocks.AspNetCore;

/// <summary>
/// Política de CORS da plataforma, baseada em lista de origens permitidas.
/// </summary>
/// <remarks>
/// <para>
/// <b>O problema corrigido aqui.</b> A configuração anterior do gateway era:
/// </para>
/// <code>
/// policy.AllowAnyHeader()
///       .AllowAnyMethod()
///       .AllowCredentials()
///       .SetIsOriginAllowed(_ => true);   // ← aceita qualquer origem
/// </code>
/// <para>
/// A combinação de <c>AllowCredentials()</c> com "qualquer origem" é
/// especificamente perigosa. O navegador <b>proíbe</b> o par
/// <c>Access-Control-Allow-Origin: *</c> + <c>Allow-Credentials: true</c> — e
/// <c>SetIsOriginAllowed(_ => true)</c> contorna essa proteção, porque em vez do
/// curinga ele devolve <i>a origem exata que pediu</i>, satisfazendo a regra do
/// navegador.
/// </para>
/// <para>
/// O efeito prático: qualquer site que a vítima visite pode fazer requisições
/// autenticadas para esta API a partir do navegador dela, com cookies e
/// credenciais anexados, e <b>ler as respostas</b>. É o vetor clássico de
/// exfiltração de dados via CORS mal configurado.
/// </para>
/// <para>
/// <b>Observação honesta:</b> neste projeto o token trafega em cabeçalho
/// <c>Authorization</c> vindo do <c>localStorage</c>, não em cookie, o que reduz
/// bastante o impacto real (o site atacante não teria o token). Ainda assim,
/// configurar CORS restritivo é o padrão correto: a política de segurança não
/// deve depender de um detalhe de implementação do cliente que pode mudar — se
/// amanhã o refresh token migrar para cookie <c>HttpOnly</c>, que é a evolução
/// recomendada, a brecha passaria a ser explorável de verdade.
/// </para>
/// </remarks>
public static class CorsExtensions
{
    /// <summary>Nome da política registrada.</summary>
    public const string PolicyName = "chat-frontend";

    /// <summary>
    /// Registra a política de CORS a partir da chave de configuração
    /// <c>Cors:AllowedOrigins</c> (lista separada por vírgulas ou array JSON).
    /// </summary>
    public static WebApplicationBuilder AddChatCors(this WebApplicationBuilder builder)
    {
        var allowedOrigins = ReadAllowedOrigins(builder.Configuration);

        builder.Services.AddCors(options => options.AddPolicy(PolicyName, policy =>
        {
            policy.AllowAnyHeader().AllowAnyMethod();

            if (allowedOrigins.Length > 0)
            {
                policy.WithOrigins(allowedOrigins)
                      // Necessário para o SignalR: o transporte WebSocket e o
                      // fallback por long polling enviam credenciais no handshake.
                      .AllowCredentials();
                return;
            }

            if (builder.Environment.IsDevelopment())
            {
                // Sem configuração e em desenvolvimento: liberamos qualquer origem
                // para facilitar o uso de `ng serve` em portas variáveis, mas SEM
                // credenciais — assim a permissividade não vira o vetor descrito acima.
                policy.AllowAnyOrigin();
                return;
            }

            // Produção sem origens configuradas: nenhuma origem cross-site é
            // aceita. Falhar fechado é a postura correta; o sintoma (frontend
            // bloqueado pelo navegador) é imediato e óbvio de diagnosticar,
            // enquanto o oposto — abrir tudo — passa despercebido para sempre.
            throw new InvalidOperationException(
                "Cors:AllowedOrigins precisa listar as origens do frontend fora do ambiente de desenvolvimento. " +
                "Exemplo: Cors__AllowedOrigins=https://chat.exemplo.com");
        }));

        return builder;
    }

    [SuppressMessage(
        "Performance",
        "CA1859:Use tipos concretos quando possível",
        Justification =
            "O analisador sugere trocar IConfiguration pelo ConfigurationManager concreto. " +
            "Aqui a abstração é intencional: este helper é compartilhado e precisa aceitar " +
            "qualquer fonte de configuração, inclusive as construídas em teste. O ganho de " +
            "desempenho seria irrelevante numa chamada única de inicialização.")]
    private static string[] ReadAllowedOrigins(IConfiguration configuration)
    {
        var section = configuration.GetSection("Cors:AllowedOrigins");

        // Aceita as duas formas: array JSON no appsettings.json e string separada
        // por vírgulas na variável de ambiente (que não representa arrays bem).
        var fromArray = section.Get<string[]>();
        if (fromArray is { Length: > 0 })
        {
            return [.. fromArray.Select(origin => origin.Trim()).Where(origin => origin.Length > 0)];
        }

        var rawValue = configuration["Cors:AllowedOrigins"];
        return string.IsNullOrWhiteSpace(rawValue)
            ? []
            : [.. rawValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }
}
