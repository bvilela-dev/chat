using MediatR;
using MessageService.Application.Abstractions;
using MessageService.Application.Contracts;

namespace MessageService.Application.Conversations;

/// <summary>
/// Query que lista as conversas do usuário autenticado.
/// </summary>
/// <remarks>
/// <para>
/// <b>Correção de segurança: IDOR no diretório de conversas.</b>
/// </para>
/// <para>
/// O endpoint anterior era <c>GET /api/users/{userId}/conversations</c>, sem
/// nenhuma verificação de que o <c>userId</c> da rota correspondia ao dono do
/// token. Trocar o GUID na URL revelava com quem qualquer outro usuário conversa
/// — um vazamento de metadados de relacionamento, que em muitos contextos é tão
/// sensível quanto o conteúdo das mensagens.
/// </para>
/// <para>
/// <b>A correção escolhida foi remover o parâmetro da rota</b>, não adicionar
/// uma checagem. O endpoint virou <c>GET /api/conversations</c> e o usuário é
/// derivado exclusivamente do JWT. A diferença entre as duas abordagens é
/// importante:
/// </para>
/// <list type="bullet">
///   <item><description>
///   <i>Checagem</i> (comparar o parâmetro com o claim) funciona, mas depende de
///   alguém lembrar de escrevê-la — e de nunca removê-la numa refatoração.
///   </description></item>
///   <item><description>
///   <i>Eliminar o parâmetro</i> torna a falha impossível por construção. Não
///   existe entrada a validar. É o princípio de "secure by design" aplicado no
///   nível do contrato da API.
///   </description></item>
/// </list>
/// </remarks>
public sealed record GetUserConversationsQuery(Guid UserId)
    : IRequest<IReadOnlyCollection<ConversationReadDto>>;

/// <summary>Monta a lista de conversas do usuário, deduplicada e ordenada.</summary>
public sealed class GetUserConversationsQueryHandler(IMessageRepository repository)
    : IRequestHandler<GetUserConversationsQuery, IReadOnlyCollection<ConversationReadDto>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyCollection<ConversationReadDto>> Handle(
        GetUserConversationsQuery request,
        CancellationToken cancellationToken)
    {
        var conversations = await repository.GetUserConversationsAsync(request.UserId, cancellationToken);

        return conversations
            // Descarta conversas diretas sem contraparte identificável: são
            // registros incompletos (uma projeção que ficou pela metade) e não
            // teriam como ser exibidos na interface.
            .Where(conversation => conversation.IsGroup || conversation.CounterpartUserId is not null)

            // DEDUPLICAÇÃO POR CONTRAPARTE.
            //
            // Sob concorrência, dois usuários que abrem a conversa um com o outro
            // ao mesmo tempo podem criar duas conversas diretas para o mesmo par:
            // ambos consultam "existe conversa?", ambos recebem "não", ambos
            // criam. Aqui a lista mostra apenas a mais recente de cada par, para
            // que a interface não exiba a mesma pessoa duas vezes.
            //
            // Isto é um paliativo de apresentação, não a correção da causa. A
            // solução definitiva é uma restrição de unicidade no banco sobre o
            // par ordenado de participantes — anotada como próximo passo no
            // README.
            .GroupBy(conversation => conversation.IsGroup
                ? $"group:{conversation.Id}"
                : $"direct:{conversation.CounterpartUserId}")
            .Select(group => group
                .OrderByDescending(conversation => conversation.LastMessageAtUtc ?? DateTime.MinValue)
                .First())

            // Atividade mais recente primeiro. Conversas ainda sem mensagem
            // ficam no fim (DateTime.MinValue como padrão de ordenação).
            .OrderByDescending(conversation => conversation.LastMessageAtUtc ?? DateTime.MinValue)
            .Select(conversation => conversation.ToDto())
            .ToArray();
    }
}
