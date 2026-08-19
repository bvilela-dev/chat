namespace IdentityService.Domain;

/// <summary>
/// Linha da tabela de outbox: um evento de integração pendente de publicação.
/// </summary>
/// <remarks>
/// <para>
/// Gravada na <b>mesma transação</b> que a alteração de negócio que a originou.
/// É esse detalhe que elimina o problema de escrita dupla descrito em
/// <c>IOutboxWriter</c>: não existe estado em que o usuário foi criado mas o
/// evento se perdeu.
/// </para>
/// <para>
/// Um despachante em segundo plano varre as linhas com
/// <see cref="ProcessedOnUtc"/> nulo, publica no RabbitMQ e as marca como
/// processadas.
/// </para>
/// </remarks>
public sealed class OutboxMessage
{
    /// <summary>Construtor exigido pelo EF Core.</summary>
    private OutboxMessage()
    {
    }

    /// <summary>
    /// Identificador da mensagem.
    /// </summary>
    /// <remarks>
    /// Reaproveita o <c>EventId</c> do evento de integração, e não um GUID novo.
    /// Assim o mesmo identificador atravessa outbox, broker e a inbox do
    /// consumidor, permitindo a deduplicação ponta a ponta.
    /// </remarks>
    public Guid Id { get; private set; }

    /// <summary>
    /// Nome do tipo do evento, usado pelo despachante para escolher como desserializar.
    /// </summary>
    /// <remarks>
    /// Guardamos o nome simples do tipo (<c>UserCreatedEvent</c>) em vez do nome
    /// qualificado com assembly. O nome qualificado quebraria a leitura de
    /// mensagens antigas assim que o assembly fosse renomeado ou tivesse a
    /// versão alterada — um problema real em sistemas que rodam por anos.
    /// </remarks>
    public string Type { get; private set; } = string.Empty;

    /// <summary>Evento serializado em JSON (coluna <c>jsonb</c> no PostgreSQL).</summary>
    public string Payload { get; private set; } = string.Empty;

    /// <summary>Instante em que o fato ocorreu, em UTC.</summary>
    public DateTime OccurredOnUtc { get; private set; }

    /// <summary>Instante da publicação bem-sucedida; <c>null</c> enquanto pendente.</summary>
    public DateTime? ProcessedOnUtc { get; private set; }

    /// <summary>Última mensagem de erro, quando a publicação falhou.</summary>
    public string? Error { get; private set; }

    /// <summary>
    /// Quantidade de tentativas de publicação já realizadas.
    /// </summary>
    /// <remarks>
    /// Serve tanto para diagnóstico quanto para alertar: uma linha com
    /// <c>RetryCount</c> alto e ainda não processada indica um evento
    /// "envenenado" que nunca vai passar e precisa de intervenção.
    /// </remarks>
    public int RetryCount { get; private set; }

    /// <summary>Cria uma entrada pendente na outbox.</summary>
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

    /// <summary>Marca a mensagem como publicada com sucesso.</summary>
    public void MarkProcessed(DateTime processedOnUtc)
    {
        ProcessedOnUtc = processedOnUtc;

        // Limpa o erro de tentativas anteriores: manter a mensagem de falha numa
        // linha já processada confundiria quem fosse investigar depois.
        Error = null;
    }

    /// <summary>Registra uma falha de publicação e incrementa o contador de tentativas.</summary>
    public void MarkFailed(string error)
    {
        // Trunca para caber com folga na coluna e para evitar que um stack trace
        // gigante inche a tabela de outbox.
        Error = error.Length > 1000 ? error[..1000] : error;
        RetryCount++;
    }
}
