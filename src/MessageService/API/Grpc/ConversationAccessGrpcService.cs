using BuildingBlocks.Contracts.Grpc;
using Grpc.Core;
using MediatR;
using MessageService.Application.Conversations;

namespace MessageService.API.Grpc;

/// <summary>
/// Serviço gRPC interno que responde consultas de participação em conversas.
/// </summary>
/// <remarks>
/// <para>
/// Consumido exclusivamente pelo Chat Service, que não tem banco próprio e
/// precisa consultar a fonte de verdade antes de autorizar uma conexão a entrar
/// numa sala de tempo real.
/// </para>
/// <para>
/// <b>Exposição.</b> Este endpoint não passa pelo API Gateway: só é alcançável
/// dentro da rede interna do cluster. Numa implantação real, o passo seguinte
/// seria autenticação mútua por TLS (mTLS) ou uma malha de serviço, para que a
/// confiança não dependa apenas do isolamento de rede.
/// </para>
/// </remarks>
public sealed class ConversationAccessGrpcService(ISender sender)
    : ConversationAccessGrpc.ConversationAccessGrpcBase
{
    /// <summary>Verifica se o usuário participa da conversa.</summary>
    public override async Task<CheckMembershipResponse> CheckMembership(
        CheckMembershipRequest request,
        ServerCallContext context)
    {
        // Identificadores malformados são resposta negativa, não erro de
        // protocolo: lançar aqui faria o cliente acionar a política de retry
        // para uma entrada que jamais vai se tornar válida.
        if (!Guid.TryParse(request.ConversationId, out var conversationId) ||
            !Guid.TryParse(request.UserId, out var userId))
        {
            return new CheckMembershipResponse { IsParticipant = false };
        }

        var isParticipant = await sender.Send(
            new CheckConversationMembershipQuery(conversationId, userId),
            context.CancellationToken);

        return new CheckMembershipResponse { IsParticipant = isParticipant };
    }
}
