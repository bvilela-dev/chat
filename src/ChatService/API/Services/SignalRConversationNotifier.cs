using ChatService.API.Hubs;
using ChatService.Application.Abstractions;
using ChatService.Application.Contracts;
using Microsoft.AspNetCore.SignalR;

namespace ChatService.API.Services;

/// <summary>
/// Implementa a entrega em tempo real usando grupos do SignalR.
/// </summary>
/// <remarks>
/// <para>
/// <b>Um grupo SignalR por conversa.</b> O nome do grupo é o identificador da
/// conversa. Transmitir para o grupo entrega a mensagem exatamente às conexões
/// inscritas, e o backplane Redis garante que isso valha para todas as réplicas,
/// não só para a que originou o envio.
/// </para>
/// <para>
/// <b>Grupo não é controle de acesso.</b> Este é o ponto que merece atenção: o
/// SignalR inscreve qualquer conexão em qualquer grupo, sem perguntar nada. Toda
/// a segurança está na verificação feita <i>antes</i> da inscrição, no
/// <c>JoinConversationCommandHandler</c>. Era exatamente essa verificação que
/// faltava na versão original — os grupos funcionavam, mas qualquer um podia
/// entrar em qualquer um deles.
/// </para>
/// <para>
/// Usa <see cref="IHubContext{THub}"/> em vez de herdar do Hub porque o envio
/// parte da camada de aplicação, fora do contexto de uma chamada de cliente.
/// </para>
/// </remarks>
public sealed class SignalRConversationNotifier(IHubContext<ChatHub> hubContext) : IConversationNotifier
{
    /// <summary>Nome do método invocado no cliente ao receber uma mensagem.</summary>
    /// <remarks>
    /// Constante em vez de literal repetido: este nome é um contrato com o
    /// frontend (<c>connection.on('messageReceived', ...)</c>). Um erro de
    /// digitação aqui não quebra o build — apenas faz as mensagens nunca
    /// chegarem, sem erro algum em lugar nenhum.
    /// </remarks>
    private const string MessageReceivedMethod = "messageReceived";

    /// <inheritdoc />
    public Task BroadcastMessageAsync(
        Guid conversationId,
        ChatRealtimeMessage message,
        CancellationToken cancellationToken)
    {
        return hubContext.Clients
            .Group(BuildGroupName(conversationId))
            .SendAsync(MessageReceivedMethod, message, cancellationToken);
    }

    /// <inheritdoc />
    public Task AddConnectionToConversationAsync(
        string connectionId,
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        return hubContext.Groups.AddToGroupAsync(connectionId, BuildGroupName(conversationId), cancellationToken);
    }

    /// <inheritdoc />
    public Task RemoveConnectionFromConversationAsync(
        string connectionId,
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        return hubContext.Groups.RemoveFromGroupAsync(connectionId, BuildGroupName(conversationId), cancellationToken);
    }

    /// <summary>Deriva o nome do grupo a partir do identificador da conversa.</summary>
    private static string BuildGroupName(Guid conversationId) => conversationId.ToString();
}
