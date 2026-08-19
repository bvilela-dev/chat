using BuildingBlocks.Contracts;

namespace IdentityService.Application.Abstractions;

/// <summary>
/// Enfileira um evento de integração na tabela de outbox, dentro da transação corrente.
/// </summary>
/// <remarks>
/// <para>
/// <b>O problema que o padrão Outbox resolve: dual write.</b> Considere o
/// cadastro de um usuário, que precisa (1) gravar a linha no banco e (2)
/// publicar <c>UserCreatedEvent</c> no RabbitMQ. São dois sistemas distintos, e
/// não existe transação distribuída entre eles. Logo, há duas formas de o
/// ingênuo falhar:
/// </para>
/// <code>
/// // Ordem A — publica primeiro
/// await broker.Publish(evento);   // ✓ publicado
/// await db.SaveChangesAsync();    // ✗ falhou
/// // Resultado: os outros serviços reagem a um usuário que NÃO EXISTE.
///
/// // Ordem B — grava primeiro
/// await db.SaveChangesAsync();    // ✓ gravado
/// await broker.Publish(evento);   // ✗ broker fora do ar
/// // Resultado: usuário criado, mas ninguém foi avisado. Inconsistência silenciosa
/// //            e permanente — o evento se perdeu, não há como reemiti-lo.
/// </code>
/// <para>
/// <b>A solução.</b> O evento é gravado como uma linha na tabela
/// <c>outbox_messages</c>, no <i>mesmo banco</i> e na <i>mesma transação</i> que
/// o usuário. Ou os dois são persistidos, ou nenhum dos dois — atomicidade
/// garantida pelo próprio PostgreSQL, sem coordenador distribuído. Depois, um
/// processo em segundo plano (<c>IdentityOutboxDispatcher</c>) lê as linhas
/// pendentes e as publica no broker, marcando-as como processadas.
/// </para>
/// <para>
/// <b>A garantia resultante é "pelo menos uma vez"</b>, e não "exatamente uma
/// vez": se o serviço morrer entre publicar e marcar como processado, o evento
/// será republicado. Por isso todo consumidor precisa ser idempotente — é a
/// função da tabela <c>inbox_messages</c> do Message Service.
/// </para>
/// </remarks>
public interface IOutboxWriter
{
    /// <summary>
    /// Adiciona o evento à outbox. Só é efetivado quando o repositório salvar as
    /// alterações — nada é publicado neste momento.
    /// </summary>
    void Add(IIntegrationEvent integrationEvent);
}
