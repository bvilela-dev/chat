using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.AspNetCore;

/// <summary>
/// Restaura o endereço real do cliente a partir dos cabeçalhos encaminhados
/// pelos proxies.
/// </summary>
/// <remarks>
/// <para>
/// <b>O defeito que isto corrige.</b> As requisições chegam aos serviços após
/// passar por dois saltos:
/// </para>
/// <code>
/// Navegador ──► nginx ──► API Gateway ──► Serviço
/// </code>
/// <para>
/// Sem este middleware, <c>HttpContext.Connection.RemoteIpAddress</c> devolve o
/// IP do <b>último salto</b> — o gateway —, e não o do usuário. Toda decisão
/// baseada em endereço de origem passa a operar sobre o valor errado.
/// </para>
/// <para>
/// No caso do rate limiting de autenticação, o efeito é grave e contraintuitivo:
/// como todos os usuários aparecem com o mesmo IP, eles compartilham uma única
/// partição. Um atacante consome as 10 tentativas por minuto e <b>bloqueia o
/// login de toda a base</b>. A proteção contra negação de serviço vira, ela
/// própria, o vetor de negação de serviço.
/// </para>
/// <para>
/// (Este comportamento foi observado em execução: com a stack completa no ar,
/// requisições vindas de origens diferentes caíam na mesma partição e eram
/// bloqueadas em conjunto.)
/// </para>
///
/// <para>
/// <b>O cuidado obrigatório: X-Forwarded-For é falsificável.</b>
/// </para>
/// <para>
/// É um cabeçalho HTTP comum, que qualquer cliente pode escrever. Se o serviço
/// confiar nele cegamente, o atacante simplesmente envia um valor diferente a
/// cada requisição e escapa do rate limiting por completo — trocando um problema
/// por outro.
/// </para>
/// <para>
/// Por isso o middleware só aceita cabeçalhos vindos de proxies <i>conhecidos</i>.
/// A configuração abaixo confia na rede interna do cluster, partindo do
/// pressuposto de que estes serviços <b>não</b> são alcançáveis diretamente de
/// fora — o tráfego externo entra obrigatoriamente pelo Ingress e pelo gateway.
/// </para>
/// <para>
/// Numa implantação real, o correto é restringir <c>KnownIPNetworks</c> ao bloco
/// CIDR dos pods, ou <c>KnownProxies</c> aos endereços do balanceador, em vez de
/// confiar na rede inteira. Fica registrado como o endurecimento seguinte.
/// </para>
/// </remarks>
public static class ForwardedHeadersExtensions
{
    /// <summary>
    /// Configura a leitura de <c>X-Forwarded-For</c> e <c>X-Forwarded-Proto</c>.
    /// </summary>
    public static WebApplicationBuilder AddChatForwardedHeaders(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders =
                // Restaura o IP de origem do cliente.
                ForwardedHeaders.XForwardedFor |
                // Restaura o esquema original (https), para que URLs geradas pela
                // aplicação e redirecionamentos não apontem para http.
                ForwardedHeaders.XForwardedProto;

            // São DOIS proxies na cadeia: nginx e API Gateway. O padrão do .NET é
            // 1, o que faria o middleware processar apenas o salto mais próximo e
            // parar no IP do gateway — exatamente o problema que se quer resolver.
            options.ForwardLimit = 2;

            // Limpar as listas faz o middleware aceitar o cabeçalho de qualquer
            // origem. É necessário em contêineres, onde os IPs dos proxies são
            // atribuídos dinamicamente e não podem ser fixados na configuração.
            //
            // Só é aceitável porque estes serviços ficam numa rede interna, sem
            // exposição direta. Se algum deles passar a ser alcançável de fora,
            // esta configuração PRECISA ser restringida — caso contrário, o
            // cliente volta a controlar o próprio identificador de rate limiting.
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        return builder;
    }

    /// <summary>
    /// Insere o middleware no pipeline.
    /// </summary>
    /// <remarks>
    /// Precisa vir <b>antes</b> de qualquer componente que leia o IP de origem —
    /// rate limiting, autenticação, log de auditoria. Registrado depois deles,
    /// não teria efeito nenhum: os cabeçalhos seriam processados tarde demais.
    /// </remarks>
    public static IApplicationBuilder UseChatForwardedHeaders(this IApplicationBuilder app)
    {
        return app.UseForwardedHeaders();
    }
}
