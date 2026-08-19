namespace MessageService.Domain;

// =============================================================================
// TABELAS DE APOIO À MENSAGERIA CONFIÁVEL
//
// Outbox e Inbox são padrões complementares que, juntos, dão a garantia prática
// de "processado exatamente uma vez" sobre um broker que só oferece "pelo menos
// uma vez":
//
//   OUTBOX  → garante que o evento NÃO SE PERCA na saída
//             (grava o evento na mesma transação do dado de negócio)
//
//   INBOX   → garante que o evento NÃO SEJA APLICADO DUAS VEZES na entrada
//             (registra o que já foi processado, e ignora repetições)
// =============================================================================

/// <summary>
/// Evento de integração pendente de publicação, gravado na mesma transação da
/// alteração de negócio que o originou.
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>Construtor exigido pelo EF Core.</summary>
    private OutboxMessage()
    {
    }

    /// <summary>Identificador da mensagem (reaproveita o EventId do evento).</summary>
    public Guid Id { get; private set; }

    /// <summary>Nome simples do tipo do evento.</summary>
    public string Type { get; private set; } = string.Empty;

    /// <summary>Evento serializado em JSON.</summary>
    public string Payload { get; private set; } = string.Empty;

    /// <summary>Instante do fato, em UTC.</summary>
    public DateTime OccurredOnUtc { get; private set; }

    /// <summary>Instante da publicação; <c>null</c> enquanto pendente.</summary>
    public DateTime? ProcessedOnUtc { get; private set; }

    /// <summary>Última falha registrada.</summary>
    public string? Error { get; private set; }

    /// <summary>Número de tentativas já realizadas.</summary>
    public int RetryCount { get; private set; }

    /// <summary>Cria uma entrada pendente.</summary>
    public static OutboxMessage Create(Guid id, string type, string payload, DateTime occurredOnUtc)
    {
        return new OutboxMessage
        {
            Id = id,
            Type = type,
            Payload = payload,
            OccurredOnUtc = occurredOnUtc
        };
    }

    /// <summary>Marca como publicada com sucesso.</summary>
    public void MarkProcessed(DateTime processedOnUtc)
    {
        ProcessedOnUtc = processedOnUtc;
        Error = null;
    }

    /// <summary>Registra uma falha e incrementa o contador de tentativas.</summary>
    public void MarkFailed(string error)
    {
        Error = error.Length > 1000 ? error[..1000] : error;
        RetryCount++;
    }
}

/// <summary>
/// Registro de que um evento já foi processado por um consumidor específico.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que a idempotência é obrigatória, e não um refinamento.</b> Toda a
/// mensageria da plataforma entrega "pelo menos uma vez". Um evento chega
/// duplicado por vários caminhos absolutamente normais:
/// </para>
/// <list type="bullet">
///   <item><description>
///   O consumidor processou a mensagem, mas caiu antes de enviar o ACK ao
///   RabbitMQ — que então a reentrega a outro consumidor.
///   </description></item>
///   <item><description>
///   O despachante da outbox publicou o evento e caiu antes de marcar a linha
///   como processada — e republica no próximo ciclo.
///   </description></item>
///   <item><description>
///   A política de retry reexecutou o consumidor após uma falha parcial.
///   </description></item>
/// </list>
/// <para>
/// Sem esta tabela, cada duplicata gravaria a mensagem de novo — e o usuário
/// veria o mesmo texto repetido no histórico.
/// </para>
/// <para>
/// <b>A chave é composta: (EventId, ConsumerName).</b> O detalhe é essencial: o
/// mesmo evento <c>MessageSentEvent</c> é consumido tanto pelo consumidor de
/// persistência quanto pelo de notificação. Se a chave fosse apenas o
/// <c>EventId</c>, o primeiro consumidor a processar bloquearia o segundo, e as
/// notificações nunca sairiam.
/// </para>
/// </remarks>
public sealed class InboxMessage
{
    /// <summary>Identificador do evento processado.</summary>
    public Guid EventId { get; private set; }

    /// <summary>Nome do consumidor que o processou.</summary>
    public string ConsumerName { get; private set; } = string.Empty;

    /// <summary>Instante do processamento, em UTC.</summary>
    /// <remarks>
    /// Além do diagnóstico, viabiliza a limpeza periódica: registros mais antigos
    /// que a janela máxima de retry do broker podem ser removidos com segurança,
    /// impedindo que a tabela cresça para sempre.
    /// </remarks>
    public DateTime ProcessedAtUtc { get; private set; }

    /// <summary>Registra o processamento de um evento.</summary>
    public static InboxMessage Create(Guid eventId, string consumerName, DateTime processedAtUtc)
    {
        return new InboxMessage
        {
            EventId = eventId,
            ConsumerName = consumerName,
            ProcessedAtUtc = processedAtUtc
        };
    }
}
