using BuildingBlocks.Contracts;
using ChatService.Application.Contracts;

namespace ChatService.Application.Abstractions;

/// <summary>
/// Entrega de mensagens e gestão de salas de tempo real.
/// </summary>
/// <remarks>
/// <para>
/// Abstrai o SignalR. A camada de aplicação fala em "transmitir para a conversa"
/// sem saber o que é um Hub, um grupo ou um WebSocket.
/// </para>
/// <para>
/// Além de testabilidade, isso é o que permitiria trocar o transporte de tempo
/// real (por WebSockets puros, Server-Sent Events ou um serviço gerenciado) sem
/// tocar em uma linha de regra de negócio.
/// </para>
/// </remarks>
public interface IConversationNotifier
{
    /// <summary>Envia a mensagem a todas as conexões inscritas na conversa.</summary>
    Task BroadcastMessageAsync(Guid conversationId, ChatRealtimeMessage message, CancellationToken cancellationToken);

    /// <summary>Inscreve uma conexão na sala da conversa.</summary>
    Task AddConnectionToConversationAsync(string connectionId, Guid conversationId, CancellationToken cancellationToken);

    /// <summary>Remove a inscrição de uma conexão.</summary>
    Task RemoveConnectionFromConversationAsync(string connectionId, Guid conversationId, CancellationToken cancellationToken);
}

/// <summary>
/// Registro de quais conexões pertencem a cada usuário.
/// </summary>
/// <remarks>
/// <para>
/// Mantido em Redis, e não em memória, porque o Chat Service roda em várias
/// réplicas: o usuário A pode estar conectado à réplica 1 e o usuário B à
/// réplica 2. Um registro local só enxergaria metade das conexões.
/// </para>
/// <para>
/// É informação distinta da presença (online/offline), que pertence ao Presence
/// Service. Aqui interessa o roteamento: "para quais sockets devo entregar isto".
/// </para>
/// </remarks>
public interface IConnectionRegistry
{
    /// <summary>Registra uma nova conexão do usuário.</summary>
    Task RegisterConnectionAsync(Guid userId, string connectionId, CancellationToken cancellationToken);

    /// <summary>Remove uma conexão encerrada.</summary>
    Task RemoveConnectionAsync(Guid userId, string connectionId, CancellationToken cancellationToken);
}

/// <summary>
/// Publicação de eventos de integração no barramento.
/// </summary>
public interface IChatEventPublisher
{
    /// <summary>Publica o evento no RabbitMQ.</summary>
    Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : class, IIntegrationEvent;
}

/// <summary>Métricas de negócio do Chat Service.</summary>
public interface IChatTelemetry
{
    /// <summary>Contabiliza a execução de um comando.</summary>
    void IncrementCommand(string commandName);

    /// <summary>Registra a abertura de uma conexão.</summary>
    void ConnectionOpened();

    /// <summary>Registra o encerramento de uma conexão.</summary>
    void ConnectionClosed();

    /// <summary>
    /// Contabiliza uma tentativa de acesso negada.
    /// </summary>
    /// <remarks>
    /// Métrica de segurança, não de desempenho. Um pico aqui indica alguém
    /// sondando identificadores de conversa — exatamente o ataque que a política
    /// de acesso passou a bloquear. É a métrica que deve disparar alerta.
    /// </remarks>
    void AccessDenied(string reason);
}
