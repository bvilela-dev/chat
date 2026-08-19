using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace BuildingBlocks.AspNetCore;

/// <summary>
/// Configuração única de observabilidade (traces + métricas) para todos os serviços.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que observabilidade é obrigatória aqui e não um extra.</b> Num
/// monólito, "por que essa mensagem demorou 3 segundos?" se responde com um
/// debugger. Numa arquitetura distribuída, uma única mensagem enviada percorre:
/// </para>
/// <code>
/// Browser → Nginx → API Gateway → Chat Service (SignalR)
///                                      ↓ publica MessageSentEvent
///                                   RabbitMQ
///                                      ↓
///                          ┌───────────┴────────────┐
///                     Message Service         Notification Service
///                     (grava + outbox)        (avisa quem está offline)
///                          ↓ RabbitMQ
///                     Message Service (projeta o read model)
/// </code>
/// <para>
/// Sem <i>tracing distribuído</i>, cada serviço só enxerga o próprio pedaço e a
/// investigação vira arqueologia de logs com timestamps. Com ele, o
/// <c>trace_id</c> atravessa HTTP <b>e</b> as mensagens do RabbitMQ (o
/// MassTransit propaga o contexto W3C nos headers automaticamente), e a
/// requisição inteira aparece como uma única cascata de spans.
/// </para>
/// <para>
/// <b>Duas saídas, dois propósitos.</b> O OTLP entrega traces e métricas ao
/// Collector (que roteia para Jaeger/Tempo); o endpoint <c>/metrics</c> é
/// raspado pelo Prometheus no modelo <i>pull</i>. Métrica responde "o sistema
/// está saudável?"; trace responde "por que esta requisição específica falhou?".
/// </para>
/// </remarks>
public static class ObservabilityExtensions
{
    /// <summary>
    /// Registra traces e métricas do OpenTelemetry para um serviço.
    /// </summary>
    /// <param name="builder">Builder da aplicação web.</param>
    /// <param name="serviceName">
    /// Nome do serviço em <c>kebab-case</c> (ex.: <c>chat-service</c>). Vira o
    /// atributo <c>service.name</c> do recurso, usado para agrupar spans e
    /// métricas por serviço nos painéis.
    /// </param>
    /// <param name="meterNames">
    /// Meters personalizados a coletar (ex.: <c>ChatService</c>). Um meter só é
    /// exportado se estiver explicitamente listado — o OpenTelemetry não coleta
    /// instrumentos desconhecidos, para evitar cardinalidade acidental.
    /// </param>
    public static WebApplicationBuilder AddChatObservability(
        this WebApplicationBuilder builder,
        string serviceName,
        params string[] meterNames)
    {
        builder.Services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName)
                // Permite comparar o comportamento entre ambientes num mesmo painel.
                .AddAttributes([
                    new KeyValuePair<string, object>("deployment.environment", builder.Environment.EnvironmentName)
                ]))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        // Health checks e scrape de métricas rodam a cada poucos
                        // segundos por réplica. Instrumentá-los inunda o backend
                        // de traces sem qualquer valor diagnóstico — e custa caro
                        // em qualquer serviço de tracing cobrado por span.
                        options.Filter = context =>
                            !context.Request.Path.StartsWithSegments("/health") &&
                            !context.Request.Path.StartsWithSegments("/metrics");

                        // Registra a exceção no span quando a requisição falha,
                        // ligando o erro diretamente à linha do trace.
                        options.RecordException = true;
                    })
                    .AddHttpClientInstrumentation()
                    // O MassTransit publica seus próprios spans (publish/consume)
                    // nesta ActivitySource. É o que costura o trace através da
                    // fronteira assíncrona do RabbitMQ.
                    .AddSource("MassTransit")
                    .AddOtlpExporter();
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    // GC, heap, thread pool, contagem de exceções: o primeiro
                    // lugar a olhar quando a latência sobe sem motivo aparente.
                    .AddRuntimeInstrumentation()
                    .AddPrometheusExporter()
                    .AddOtlpExporter();

                foreach (var meterName in meterNames)
                {
                    metrics.AddMeter(meterName);
                }
            });

        return builder;
    }
}
