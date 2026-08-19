namespace ChatService.Application.Abstractions;

/// <summary>
/// Decide se um usuário pode participar de uma conversa em tempo real.
/// </summary>
/// <remarks>
/// <para>
/// <b>Esta abstração corrige a falha de segurança mais grave do projeto.</b>
/// </para>
/// <para>
/// O Hub SignalR original tinha <c>[Authorize]</c>, o que garantia apenas que
/// quem chamava possuía um token válido. Nenhum método verificava se o usuário
/// tinha qualquer relação com a conversa informada. Na prática:
/// </para>
/// <code>
/// // Qualquer usuário autenticado, a partir do console do navegador:
/// await connection.invoke("JoinConversation", "&lt;guid-de-conversa-alheia&gt;");
/// // → entra na sala e passa a RECEBER EM TEMPO REAL todas as mensagens
/// //   trocadas naquela conversa privada.
///
/// await connection.invoke("SendMessage", { conversationId: "&lt;guid-alheio&gt;",
///                                          content: "..." });
/// // → INJETA uma mensagem numa conversa da qual não participa.
/// </code>
/// <para>
/// Identificadores de conversa não são segredo: eles aparecem nas respostas da
/// API e nas URLs do frontend. Tratá-los como se fossem — o antipadrão de
/// "segurança por obscuridade" — é o que tornava a falha explorável.
/// </para>
/// <para>
/// A política é uma abstração, e não uma chamada gRPC direta no handler, por
/// dois motivos: mantém a camada de aplicação ignorante quanto ao transporte, e
/// permite que os testes verifiquem os caminhos de permissão e de negação sem
/// levantar servidor algum.
/// </para>
/// </remarks>
public interface IConversationAccessPolicy
{
    /// <summary>
    /// Indica se o usuário participa da conversa.
    /// </summary>
    /// <remarks>
    /// A implementação consulta o Message Service — a fonte de verdade — com um
    /// cache de curta duração para não transformar cada mensagem enviada numa
    /// chamada de rede.
    /// </remarks>
    Task<bool> CanAccessConversationAsync(Guid conversationId, Guid userId, CancellationToken cancellationToken);
}
