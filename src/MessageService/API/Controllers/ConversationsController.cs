using BuildingBlocks.AspNetCore;
using MediatR;
using MessageService.Application.Contracts;
using MessageService.Application.Conversations;
using MessageService.Application.Messages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MessageService.API.Controllers;

/// <summary>Corpo da requisição de criação de conversa direta.</summary>
/// <param name="ParticipantId">Usuário com quem se quer conversar.</param>
public sealed record CreateDirectConversationRequest(Guid ParticipantId);

/// <summary>
/// Conversas do usuário autenticado.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nota de segurança sobre o desenho das rotas.</b> Nenhum endpoint deste
/// controller aceita um identificador de usuário como parâmetro. O usuário é
/// sempre derivado do token JWT.
/// </para>
/// <para>
/// Isso corrige duas falhas de controle de acesso (IDOR) da versão anterior:
/// </para>
/// <code>
/// # ANTES — qualquer usuário autenticado lia dados de qualquer outro
/// GET /api/users/{userId}/conversations        → conversas de terceiros
/// GET /api/conversations/{id}/messages         → histórico de conversa alheia
///
/// # DEPOIS
/// GET /api/conversations                       → derivado do token; sem parâmetro a burlar
/// GET /api/conversations/{id}/messages         → 403 se o solicitante não participa
/// </code>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/conversations")]
[Produces("application/json")]
public sealed class ConversationsController(ISender sender) : ControllerBase
{
    /// <summary>Lista as conversas do usuário autenticado.</summary>
    /// <remarks>
    /// A rota não recebe identificador de usuário: ele vem do token. É a
    /// diferença entre "validar o parâmetro" e "não ter parâmetro para
    /// validar" — a segunda opção torna a falha impossível por construção.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType<IReadOnlyCollection<ConversationReadDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyCollection<ConversationReadDto>> GetMyConversations(CancellationToken cancellationToken)
    {
        return sender.Send(new GetUserConversationsQuery(User.GetRequiredUserId()), cancellationToken);
    }

    /// <summary>Abre (ou reaproveita) a conversa direta com outro usuário.</summary>
    /// <response code="200">Conversa criada ou já existente.</response>
    /// <response code="400">Identificador ausente, ou tentativa de conversar consigo mesmo.</response>
    [HttpPost("direct")]
    [ProducesResponseType<ConversationReadDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<ConversationReadDto> CreateDirectConversation(
        [FromBody] CreateDirectConversationRequest request,
        CancellationToken cancellationToken)
    {
        // O comando é montado explicitamente, e o iniciador vem do token.
        //
        // Vincular o comando direto do corpo da requisição, como faz o endpoint
        // de cadastro, seria uma brecha de "mass assignment" aqui: o cliente
        // poderia informar um `InitiatorId` arbitrário e criar conversas em nome
        // de terceiros.
        return sender.Send(
            new CreateDirectConversationCommand(User.GetRequiredUserId(), request.ParticipantId),
            cancellationToken);
    }

    /// <summary>Lê o histórico paginado de uma conversa.</summary>
    /// <param name="conversationId">Conversa consultada.</param>
    /// <param name="page">Página, começando em 1.</param>
    /// <param name="pageSize">Itens por página (máximo 200).</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <response code="200">Página do histórico.</response>
    /// <response code="400">Parâmetros de paginação inválidos.</response>
    /// <response code="403">O usuário autenticado não participa desta conversa.</response>
    [HttpGet("{conversationId:guid}/messages")]
    [ProducesResponseType<IReadOnlyCollection<MessageReadDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<IReadOnlyCollection<MessageReadDto>> GetMessages(
        Guid conversationId,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        CancellationToken cancellationToken)
    {
        // Valores omitidos assumem os padrões. A validação real (limites e
        // faixas) é do FluentValidation, no ValidationBehavior — aqui é apenas a
        // tradução de "parâmetro ausente" (que chega como 0) para o padrão.
        return sender.Send(
            new GetMessagesByConversationQuery(
                conversationId,
                RequesterId: User.GetRequiredUserId(),
                Page: page <= 0 ? 1 : page,
                PageSize: pageSize <= 0 ? 50 : pageSize),
            cancellationToken);
    }
}
