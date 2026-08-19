namespace ChatService.Application.Contracts;

/// <summary>
/// Mensagem entregue aos clientes conectados via SignalR.
/// </summary>
/// <remarks>
/// <para>
/// Este é o formato que trafega pelo WebSocket. Ele é <b>propositalmente
/// distinto</b> do <c>MessageSentEvent</c> publicado no RabbitMQ, mesmo que hoje
/// os campos coincidam quase por completo.
/// </para>
/// <para>
/// O motivo: são dois contratos com públicos e ciclos de vida diferentes. O
/// contrato de tempo real é consumido pelo frontend e pode mudar junto com ele,
/// num único deploy. O evento de integração é consumido por outros serviços e só
/// pode evoluir de forma retrocompatível. Fundi-los num tipo só acoplaria a
/// evolução da interface à do barramento de eventos.
/// </para>
/// </remarks>
public sealed record ChatRealtimeMessage(
    Guid MessageId,
    Guid ConversationId,
    Guid SenderId,
    string SenderName,
    string Content,
    DateTime CreatedAtUtc);
