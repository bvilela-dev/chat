using BuildingBlocks.Contracts;
using MessageService.Domain;

namespace MessageService.Application.Contracts;

/// <summary>Mensagem como o cliente a enxerga.</summary>
public sealed record MessageReadDto(
    Guid Id,
    Guid ConversationId,
    Guid SenderId,
    string SenderName,
    string Content,
    DateTime CreatedAtUtc);

/// <summary>Resumo de conversa exibido na lista lateral.</summary>
/// <param name="Id">Identificador da conversa.</param>
/// <param name="LastMessage">Prévia da última mensagem.</param>
/// <param name="LastMessageAtUtc">Instante da última mensagem.</param>
/// <param name="IsGroup">Indica se é conversa em grupo.</param>
/// <param name="CounterpartUserId">
/// Em conversa direta, o outro participante. É o que permite ao frontend exibir
/// o nome da pessoa em vez do identificador da conversa.
/// </param>
public sealed record ConversationReadDto(
    Guid Id,
    string LastMessage,
    DateTime? LastMessageAtUtc,
    bool IsGroup,
    Guid? CounterpartUserId);

/// <summary>
/// Resumo interno de conversa, montado pelo repositório.
/// </summary>
/// <remarks>
/// Tipo separado do <see cref="ConversationReadDto"/> de propósito: este é o
/// formato produzido pela consulta ao banco, aquele é o contrato publicado na
/// API. Manter os dois distintos evita que uma mudança de schema vaze
/// diretamente para o contrato externo.
/// </remarks>
public sealed record ConversationSummary(
    Guid Id,
    string LastMessage,
    DateTime? LastMessageAtUtc,
    bool IsGroup,
    Guid? CounterpartUserId);

/// <summary>
/// Evento interno que solicita a atualização do read model de uma mensagem.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que a projeção passa pelo broker em vez de ser feita na hora.</b> Ao
/// persistir a mensagem, o serviço poderia atualizar as projeções na mesma
/// transação. Publicar um evento e processá-lo depois traz três vantagens:
/// </para>
/// <list type="number">
///   <item><description>
///   A transação de escrita fica curta — menos tempo segurando locks na tabela
///   de mensagens, que é a mais quente do sistema.
///   </description></item>
///   <item><description>
///   A projeção pode ser reconstruída do zero reprocessando os eventos, sem
///   tocar no modelo de escrita.
///   </description></item>
///   <item><description>
///   Novas projeções (busca full-text, contadores de não lidas) passam a ser
///   novos consumidores, sem alterar o caminho de escrita.
///   </description></item>
/// </list>
/// <para>
/// O custo é a consistência eventual, discutida em <c>ReadModel.cs</c>.
/// </para>
/// </remarks>
public sealed record MessageProjectionRequested(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid MessageId,
    Guid ConversationId,
    Guid SenderId,
    string SenderName,
    string Content,
    DateTime MessageCreatedAtUtc) : IIntegrationEvent;

/// <summary>Conversões entre entidades de leitura e DTOs.</summary>
public static class MessageMapping
{
    /// <summary>Projeta o read model no DTO da API.</summary>
    public static MessageReadDto ToDto(this MessageReadModel message)
    {
        return new MessageReadDto(
            message.Id,
            message.ConversationId,
            message.SenderId,
            message.SenderName,
            message.Content,
            message.CreatedAtUtc);
    }

    /// <summary>Projeta uma coleção de mensagens.</summary>
    public static IReadOnlyCollection<MessageReadDto> ToDtos(this IEnumerable<MessageReadModel> messages)
    {
        return [.. messages.Select(ToDto)];
    }

    /// <summary>Converte o resumo interno no DTO publicado.</summary>
    public static ConversationReadDto ToDto(this ConversationSummary summary)
    {
        return new ConversationReadDto(
            summary.Id,
            summary.LastMessage,
            summary.LastMessageAtUtc,
            summary.IsGroup,
            summary.CounterpartUserId);
    }
}
