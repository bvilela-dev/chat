using MediatR;
using MessageService.Application.Abstractions;

namespace MessageService.Application.Conversations;

/// <summary>
/// Query de autorização: o usuário participa da conversa?
/// </summary>
/// <remarks>
/// <para>
/// Consumida pelo Chat Service via gRPC antes de permitir que uma conexão entre
/// numa sala de tempo real ou envie mensagem.
/// </para>
/// <para>
/// <b>Por que o Chat Service precisa perguntar.</b> Ele não tem banco próprio:
/// só mantém conexões e publica eventos. A verdade sobre quem participa de qual
/// conversa mora no Message Service. Como cada serviço é dono dos seus dados —
/// e consultar o banco alheio está fora de questão —, a pergunta é feita pelo
/// contrato explícito do gRPC.
/// </para>
/// <para>
/// É um dos poucos pontos em que a comunicação síncrona se justifica: uma
/// decisão de autorização precisa ser tomada <i>agora</i> e com o dado
/// <i>atual</i>. Propagar essa informação por evento significaria decidir com
/// base numa cópia possivelmente defasada — e, em autorização, uma cópia
/// defasada é uma falha de segurança.
/// </para>
/// </remarks>
public sealed record CheckConversationMembershipQuery(Guid ConversationId, Guid UserId) : IRequest<bool>;

/// <summary>Responde à consulta de participação.</summary>
public sealed class CheckConversationMembershipQueryHandler(IMessageRepository repository)
    : IRequestHandler<CheckConversationMembershipQuery, bool>
{
    /// <inheritdoc />
    public Task<bool> Handle(CheckConversationMembershipQuery request, CancellationToken cancellationToken)
    {
        return repository.IsParticipantAsync(request.ConversationId, request.UserId, cancellationToken);
    }
}
