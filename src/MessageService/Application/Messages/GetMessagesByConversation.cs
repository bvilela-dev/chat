using BuildingBlocks.Application;
using FluentValidation;
using MediatR;
using MessageService.Application.Abstractions;
using MessageService.Application.Contracts;

namespace MessageService.Application.Messages;

/// <summary>
/// Query paginada do histórico de uma conversa.
/// </summary>
/// <param name="ConversationId">Conversa consultada.</param>
/// <param name="RequesterId">
/// Usuário que está pedindo o histórico, sempre derivado do token JWT.
/// </param>
/// <param name="Page">Página, começando em 1.</param>
/// <param name="PageSize">Itens por página.</param>
/// <remarks>
/// <para>
/// <b>Correção de segurança: IDOR (Insecure Direct Object Reference).</b>
/// </para>
/// <para>
/// A versão anterior desta query recebia apenas o <c>ConversationId</c>. O
/// endpoint exigia autenticação, mas <b>não checava autorização</b>: qualquer
/// usuário logado que trocasse o GUID na URL lia o histórico completo de
/// qualquer conversa da plataforma, incluindo conversas privadas entre
/// terceiros.
/// </para>
/// <code>
/// # Antes: 200 OK com as mensagens de uma conversa alheia
/// GET /messages/api/conversations/{guid-de-outra-pessoa}/messages
/// Authorization: Bearer &lt;token-válido-de-qualquer-usuário&gt;
/// </code>
/// <para>
/// É o item nº 1 do OWASP Top 10 (Broken Access Control), e a falha mais comum
/// em APIs REST. A causa raiz costuma ser exatamente esta: confundir
/// <i>autenticação</i> ("o token é válido") com <i>autorização</i> ("este
/// usuário pode ver este recurso específico").
/// </para>
/// <para>
/// O <c>RequesterId</c> ser parte da query — e não um parâmetro opcional — faz o
/// compilador cobrar a informação em todo ponto de chamada. Não há como
/// esquecer de passá-la.
/// </para>
/// </remarks>
public sealed record GetMessagesByConversationQuery(
    Guid ConversationId,
    Guid RequesterId,
    int Page = 1,
    int PageSize = 50) : IRequest<IReadOnlyCollection<MessageReadDto>>;

/// <summary>Regras de paginação.</summary>
public sealed class GetMessagesByConversationQueryValidator : AbstractValidator<GetMessagesByConversationQuery>
{
    /// <summary>Configura as regras.</summary>
    public GetMessagesByConversationQueryValidator()
    {
        RuleFor(query => query.ConversationId).NotEmpty();
        RuleFor(query => query.RequesterId).NotEmpty();
        RuleFor(query => query.Page).GreaterThan(0);

        // O teto de 200 não é estético: sem ele, `?pageSize=1000000` faria o
        // serviço materializar a tabela inteira em memória. É um vetor de
        // negação de serviço trivial de explorar em qualquer API paginada.
        RuleFor(query => query.PageSize).InclusiveBetween(1, 200);
    }
}

/// <summary>Lê o histórico após confirmar que o solicitante participa da conversa.</summary>
public sealed class GetMessagesByConversationQueryHandler(IMessageRepository repository)
    : IRequestHandler<GetMessagesByConversationQuery, IReadOnlyCollection<MessageReadDto>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyCollection<MessageReadDto>> Handle(
        GetMessagesByConversationQuery request,
        CancellationToken cancellationToken)
    {
        // A VERIFICAÇÃO DE AUTORIZAÇÃO VEM ANTES DE QUALQUER LEITURA DE DADO.
        //
        // Colocá-la aqui, no handler, e não no controller, é uma decisão
        // deliberada: a regra passa a valer para toda porta de entrada. Se
        // amanhã esta query for chamada por um endpoint gRPC ou por um job em
        // lote, a proteção vai junto.
        var isParticipant = await repository.IsParticipantAsync(
            request.ConversationId,
            request.RequesterId,
            cancellationToken);

        if (!isParticipant)
        {
            // 403, e com uma mensagem que não distingue "a conversa não existe"
            // de "existe mas você não participa". Diferenciar os dois casos
            // permitiria sondar quais identificadores de conversa são válidos.
            throw new ForbiddenException("Você não participa desta conversa.");
        }

        var messages = await repository.GetMessagesByConversationAsync(
            request.ConversationId,
            request.Page,
            request.PageSize,
            cancellationToken);

        return messages.ToDtos();
    }
}
