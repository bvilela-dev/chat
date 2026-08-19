using BuildingBlocks.Application;
using BuildingBlocks.AspNetCore;
using ChatService.Application.Abstractions;
using ChatService.Application.Contracts;
using ChatService.Application.Conversations;
using ChatService.Application.Messages;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ChatService.API.Hubs;

/// <summary>Payload de envio de mensagem recebido do cliente.</summary>
public sealed record SendMessageRequest(Guid ConversationId, string Content);

/// <summary>
/// Hub SignalR: porta de entrada de tempo real do chat.
/// </summary>
/// <remarks>
/// <para>
/// <b>O papel do Hub é ser uma casca fina.</b> Ele traduz chamadas do WebSocket
/// em comandos do MediatR e nada mais — nenhuma regra de negócio vive aqui.
/// Isso permite que toda a lógica de envio e de autorização seja testada sem
/// levantar servidor, WebSocket ou Redis.
/// </para>
/// <para>
/// <b>Como o SignalR escala horizontalmente.</b> Cada réplica só conhece as
/// conexões ligadas a ela. Sem coordenação, uma mensagem transmitida pela
/// réplica 1 nunca chegaria a um usuário conectado à réplica 2. O <i>backplane</i>
/// Redis resolve isso: a transmissão é publicada num canal Redis e todas as
/// réplicas a repassam às suas próprias conexões.
/// </para>
/// <code>
/// Usuário A ──► Réplica 1 ──► Redis (pub/sub) ──► Réplica 2 ──► Usuário B
/// </code>
/// <para>
/// <b>Autenticação no handshake.</b> O <c>[Authorize]</c> vale para a conexão
/// inteira: um cliente sem token válido não completa o handshake e nenhum método
/// do Hub é alcançável. O token chega pela query string porque a API de WebSocket
/// do navegador não permite cabeçalhos personalizados — ver
/// <c>JwtAuthenticationExtensions</c>.
/// </para>
/// </remarks>
[Authorize]
public sealed class ChatHub(
    ISender sender,
    IConnectionRegistry connectionRegistry,
    IChatTelemetry telemetry,
    ILogger<ChatHub> logger)
    : Hub
{
    /// <summary>Registra a conexão recém-estabelecida.</summary>
    public override async Task OnConnectedAsync()
    {
        var userId = Context.User.GetRequiredUserId();

        await connectionRegistry.RegisterConnectionAsync(userId, Context.ConnectionId, Context.ConnectionAborted);
        telemetry.ConnectionOpened();

        logger.LogInformation(
            "Conexão {ConnectionId} estabelecida para o usuário {UserId}.",
            Context.ConnectionId,
            userId);

        await base.OnConnectedAsync();
    }

    /// <summary>Remove o registro da conexão encerrada.</summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // TryGet, e não GetRequired: no encerramento o contexto de usuário pode
        // já estar parcialmente desmontado (queda abrupta, token expirado durante
        // a sessão). Lançar aqui impediria a limpeza do registro e deixaria uma
        // conexão fantasma no Redis — que só sumiria pelo TTL.
        if (Context.User.TryGetUserId(out var userId))
        {
            await connectionRegistry.RemoveConnectionAsync(userId, Context.ConnectionId, CancellationToken.None);
        }

        telemetry.ConnectionClosed();

        if (exception is not null)
        {
            logger.LogWarning(
                exception,
                "Conexão {ConnectionId} encerrada com erro.",
                Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>Envia uma mensagem para a conversa informada.</summary>
    /// <remarks>
    /// O remetente vem <b>sempre</b> do token, nunca do payload. Aceitar um
    /// <c>senderId</c> vindo do cliente permitiria a qualquer usuário enviar
    /// mensagens se passando por outro — falsificação de identidade trivial.
    /// </remarks>
    public Task<ChatRealtimeMessage> SendMessage(SendMessageRequest request)
    {
        return ExecuteAsync(nameof(SendMessage), () => sender.Send(
            new SendMessageCommand(
                request.ConversationId,
                Context.User.GetRequiredUserId(),
                Context.User.GetDisplayName(),
                request.Content),
            Context.ConnectionAborted));
    }

    /// <summary>Inscreve esta conexão na sala de uma conversa.</summary>
    /// <remarks>
    /// A participação é verificada no handler antes da inscrição. Sem essa
    /// checagem, qualquer usuário autenticado poderia entrar em qualquer conversa
    /// informando o identificador — ver <see cref="IConversationAccessPolicy"/>.
    /// </remarks>
    public Task JoinConversation(Guid conversationId)
    {
        return ExecuteAsync(nameof(JoinConversation), () => sender.Send(
            new JoinConversationCommand(conversationId, Context.User.GetRequiredUserId(), Context.ConnectionId),
            Context.ConnectionAborted));
    }

    /// <summary>Remove esta conexão da sala de uma conversa.</summary>
    public Task LeaveConversation(Guid conversationId)
    {
        return ExecuteAsync(nameof(LeaveConversation), () => sender.Send(
            new LeaveConversationCommand(conversationId, Context.User.GetRequiredUserId(), Context.ConnectionId),
            Context.ConnectionAborted));
    }

    /// <summary>Executa uma operação sem retorno, traduzindo falhas para o cliente.</summary>
    private async Task ExecuteAsync(string operationName, Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (Exception exception)
        {
            throw TranslateToHubException(operationName, exception);
        }
    }

    /// <summary>Executa uma operação com retorno, traduzindo falhas para o cliente.</summary>
    private async Task<TResponse> ExecuteAsync<TResponse>(string operationName, Func<Task<TResponse>> operation)
    {
        try
        {
            return await operation();
        }
        catch (Exception exception)
        {
            throw TranslateToHubException(operationName, exception);
        }
    }

    /// <summary>
    /// Converte uma exceção em <see cref="HubException"/>, decidindo o que pode
    /// ser revelado ao cliente.
    /// </summary>
    /// <remarks>
    /// <para>
    /// O SignalR tem um comportamento próprio no tratamento de erro: apenas a
    /// mensagem de uma <see cref="HubException"/> chega ao cliente. Qualquer
    /// outra exceção vira o texto genérico "An unexpected error occurred".
    /// </para>
    /// <para>
    /// Isso é seguro por padrão — não vaza detalhe interno —, mas inutilizável
    /// para erros de negócio: quem tenta enviar uma mensagem longa demais precisa
    /// saber <i>por que</i> falhou. Este método faz a tradução explícita: falhas
    /// conhecidas viram mensagens legíveis; defeitos inesperados permanecem
    /// opacos, com o detalhe indo apenas para o log.
    /// </para>
    /// </remarks>
    private HubException TranslateToHubException(string operationName, Exception exception)
    {
        switch (exception)
        {
            case ValidationException validationException:
            {
                // Erro de formato: as mensagens do validador foram escritas para
                // serem lidas pelo usuário final.
                var details = string.Join(" ", validationException.Errors.Select(error => error.ErrorMessage));

                logger.LogInformation(
                    "{OperationName} rejeitado por validação na conexão {ConnectionId}: {Details}",
                    operationName,
                    Context.ConnectionId,
                    details);

                return new HubException(details);
            }

            case ApplicationRuleException ruleException:
            {
                // Falha de negócio prevista — tipicamente, acesso negado à conversa.
                logger.LogWarning(
                    "{OperationName} negado na conexão {ConnectionId}: {Message}",
                    operationName,
                    Context.ConnectionId,
                    ruleException.Message);

                return new HubException(ruleException.Message);
            }

            default:
            {
                // Defeito inesperado: registra tudo no log e devolve mensagem
                // genérica. Repassar `exception.Message` poderia expor host de
                // banco, nome de fila ou caminho de arquivo ao cliente.
                logger.LogError(
                    exception,
                    "{OperationName} falhou na conexão {ConnectionId}.",
                    operationName,
                    Context.ConnectionId);

                return new HubException("Não foi possível concluir a operação. Tente novamente.");
            }
        }
    }
}
