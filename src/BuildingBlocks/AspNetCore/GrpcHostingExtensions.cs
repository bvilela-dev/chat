using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.AspNetCore;

/// <summary>
/// Configura portas separadas para REST (HTTP/1.1) e gRPC (HTTP/2 em cleartext).
/// </summary>
/// <remarks>
/// <para>
/// <b>O problema.</b> Identity e Message Service expõem duas superfícies:
/// controllers REST, consumidos pelo frontend, e serviços gRPC, consumidos por
/// outros microsserviços. gRPC exige HTTP/2.
/// </para>
///
/// <para>
/// <b>Por que uma porta só não resolve — e a mensagem do Kestrel que revela isso.</b>
/// </para>
/// <para>
/// A tentativa natural é declarar a porta única como
/// <c>HttpProtocols.Http1AndHttp2</c>. O servidor sobe sem erro, mas o Kestrel
/// emite este aviso, fácil de perder no meio do log de inicialização:
/// </para>
/// <code>
/// HTTP/2 is not enabled for [::]:8080. The endpoint is configured to use
/// HTTP/1.1 and HTTP/2, but TLS is not enabled. HTTP/2 requires TLS application
/// protocol negotiation. Connections to this endpoint will use HTTP/1.1.
/// </code>
/// <para>
/// Ou seja: em <b>cleartext</b>, o Kestrel só ativa HTTP/2 numa porta declarada
/// como <c>Http2</c> <i>exclusivamente</i>. Com <c>Http1AndHttp2</c> e sem TLS,
/// ele não tem como negociar por ALPN e escolhe HTTP/1.1 para todas as conexões —
/// respondendo à tentativa de h2c com <c>GOAWAY / HTTP_1_1_REQUIRED (0xd)</c>.
/// </para>
///
/// <para>
/// <b>Por que o sintoma era enganoso.</b> A política de acesso do Chat Service
/// <i>falha fechada</i>: sem conseguir confirmar a participação, ela nega. Com o
/// gRPC quebrado, TODA entrada em conversa era recusada — inclusive a dos
/// participantes legítimos.
/// </para>
/// <para>
/// O mais traiçoeiro é que a falha se disfarçava de sucesso: as tentativas de
/// acesso indevido continuavam sendo bloqueadas, então uma verificação
/// superficial concluiria que a segurança estava funcionando. Só ao exercitar o
/// <b>fluxo legítimo</b> ponta a ponta o defeito apareceu — razão pela qual um
/// teste de caminho feliz vale tanto quanto um de caminho negativo.
/// </para>
///
/// <para>
/// <b>A solução: duas portas.</b> 8080 para REST em HTTP/1.1 e 8081 para gRPC em
/// HTTP/2 puro. É também a recomendação da documentação da Microsoft para gRPC
/// sem TLS, e traz um benefício de segurança: a porta gRPC nunca é publicada
/// fora da rede interna, ficando naturalmente inacessível de fora do cluster.
/// </para>
/// <para>
/// A alternativa seria habilitar TLS entre os serviços — o que resolveria a
/// negociação por ALPN e permitiria porta única. É o caminho correto em produção
/// (via mTLS ou uma malha de serviço), e fica registrado como o passo seguinte.
/// </para>
/// </remarks>
public static class GrpcHostingExtensions
{
    /// <summary>Porta padrão do tráfego REST/HTTP.</summary>
    public const int DefaultRestPort = 8080;

    /// <summary>Porta padrão do tráfego gRPC.</summary>
    public const int DefaultGrpcPort = 8081;

    /// <summary>
    /// Vincula uma porta HTTP/1.1 para REST e uma porta HTTP/2 para gRPC.
    /// </summary>
    /// <param name="builder">Builder da aplicação web.</param>
    /// <param name="restPort">Porta do tráfego REST.</param>
    /// <param name="grpcPort">Porta do tráfego gRPC.</param>
    /// <remarks>
    /// Deve ser chamado por serviços que expõem REST <b>e</b> gRPC. Serviços
    /// apenas REST não precisam — e não ganham nada com isso.
    /// </remarks>
    public static WebApplicationBuilder AddChatGrpcAndRestHosting(
        this WebApplicationBuilder builder,
        int restPort = DefaultRestPort,
        int grpcPort = DefaultGrpcPort)
    {
        builder.Services.Configure<KestrelServerOptions>(options =>
        {
            // ListenAnyIP explícito, e não ConfigureEndpointDefaults.
            //
            // `ConfigureEndpointDefaults` só se aplica a endpoints declarados via
            // `Kestrel:Endpoints` na configuração ou via `Listen(...)` em código —
            // não alcança as portas vindas de `ASPNETCORE_URLS`, que é como os
            // contêineres deste projeto são configurados.
            //
            // Declarar as portas aqui faz o Kestrel ignorar `ASPNETCORE_URLS`
            // (ele registra isso no log como "Overriding address(es)"), o que é o
            // comportamento desejado: as portas passam a ser definidas num único
            // lugar.
            options.ListenAnyIP(restPort, endpoint =>
            {
                // HTTP/1.1 explícito: é o que o navegador e o WebSocket usam.
                endpoint.Protocols = HttpProtocols.Http1;
            });

            options.ListenAnyIP(grpcPort, endpoint =>
            {
                // Http2 EXCLUSIVO — é o único modo em que o Kestrel aceita HTTP/2
                // sem TLS (h2c com "prior knowledge", que é exatamente o que o
                // cliente gRPC usa para endereços `http://`).
                endpoint.Protocols = HttpProtocols.Http2;
            });
        });

        return builder;
    }
}
