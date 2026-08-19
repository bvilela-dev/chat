using MassTransit;
using Microsoft.Extensions.Configuration;

namespace BuildingBlocks.Messaging;

/// <summary>
/// Políticas de resiliência aplicadas a todo consumo de mensagens da plataforma.
/// </summary>
/// <remarks>
/// <para>
/// Numa arquitetura orientada a eventos, falha não é exceção: é rotina. O banco
/// reinicia, a rede oscila, um serviço é reimplantado no meio do processamento.
/// A pergunta de projeto não é "como evitar falhas", e sim <b>"o que acontece com
/// a mensagem quando algo falha"</b>. Estas três camadas respondem isso:
/// </para>
/// <list type="number">
///   <item><description>
///   <b>Retry com backoff exponencial</b> — absorve falhas transitórias (o
///   deadlock de banco que some na segunda tentativa).
///   </description></item>
///   <item><description>
///   <b>Circuit breaker</b> — quando a falha <i>não</i> é transitória, para de
///   tentar por um tempo em vez de martelar um recurso já caído.
///   </description></item>
///   <item><description>
///   <b>Dead-letter queue</b> — a mensagem que esgotou as tentativas é preservada
///   para análise, em vez de descartada silenciosamente.
///   </description></item>
/// </list>
/// </remarks>
public static class MessagingResilience
{
    /// <summary>
    /// Exchange de dead-letter para onde vão as mensagens que não puderam ser processadas.
    /// </summary>
    public const string DeadLetterExchange = "chat.dlx";

    /// <summary>
    /// Aplica retry, circuit breaker e roteamento de dead-letter a um endpoint de consumo.
    /// </summary>
    public static void ConfigureResilience(this IRabbitMqReceiveEndpointConfigurator endpoint)
    {
        // Ao esgotar as tentativas, o RabbitMQ encaminha a mensagem para esta
        // exchange em vez de descartá-la. Sem isso, um bug de desserialização
        // apagaria mensagens de usuários sem deixar rastro. Com isso, elas ficam
        // disponíveis para inspeção e reprocessamento depois da correção.
        endpoint.SetQueueArgument("x-dead-letter-exchange", DeadLetterExchange);

        endpoint.ConfigureResilienceInternal();
    }

    /// <summary>
    /// Aplica retry e circuit breaker no nível do barramento.
    /// </summary>
    /// <remarks>
    /// Usada pelos serviços que apenas <i>publicam</i> eventos e não declaram
    /// endpoints de consumo próprios.
    /// </remarks>
    public static void ConfigureResilience(this IRabbitMqBusFactoryConfigurator bus)
    {
        bus.ConfigureResilienceInternal();
    }

    private static void ConfigureResilienceInternal(this IConsumePipeConfigurator pipe)
    {
        // 3 tentativas com intervalos crescentes: ~1s, ~3s, ~7s (até o teto de 15s).
        //
        // O backoff EXPONENCIAL é o ponto importante. Repetir de imediato, em
        // intervalo fixo, produz o "thundering herd": todos os consumidores
        // voltam ao mesmo tempo sobre um serviço que ainda está se recuperando e
        // o derrubam de novo. O espaçamento crescente dá tempo real de
        // recuperação entre as tentativas.
        pipe.UseMessageRetry(retry => retry.Exponential(
            retryLimit: 3,
            minInterval: TimeSpan.FromSeconds(1),
            maxInterval: TimeSpan.FromSeconds(15),
            intervalDelta: TimeSpan.FromSeconds(2)));

        pipe.UseCircuitBreaker(breaker =>
        {
            // Só começa a avaliar depois de 5 mensagens na janela: com volume
            // baixo, 1 falha em 2 mensagens é 50% de erro e abriria o circuito
            // por puro ruído estatístico.
            breaker.ActiveThreshold = 5;

            // Percentual de falha que abre o circuito.
            breaker.TripThreshold = 15;

            // Janela de observação das falhas.
            breaker.TrackingPeriod = TimeSpan.FromMinutes(1);

            // Tempo em circuito aberto antes de testar a recuperação.
            breaker.ResetInterval = TimeSpan.FromMinutes(1);
        });
    }

    /// <summary>
    /// Configura o host RabbitMQ a partir da seção <c>RabbitMq</c> da configuração.
    /// </summary>
    /// <remarks>
    /// As credenciais <c>guest/guest</c> como padrão servem apenas ao ambiente
    /// local: o próprio RabbitMQ recusa esse usuário em conexões que não venham
    /// de <c>localhost</c>, então não há risco de o padrão "funcionar por acidente"
    /// em produção.
    /// </remarks>
    public static void ConfigureRabbitMqHost(
        this IRabbitMqBusFactoryConfigurator bus,
        IConfiguration configuration)
    {
        bus.Host(
            configuration["RabbitMq:Host"] ?? "localhost",
            configuration["RabbitMq:VirtualHost"] ?? "/",
            host =>
            {
                host.Username(configuration["RabbitMq:Username"] ?? "guest");
                host.Password(configuration["RabbitMq:Password"] ?? "guest");
            });
    }
}
