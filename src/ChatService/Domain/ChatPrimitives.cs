namespace ChatService.Domain;

// =============================================================================
// O DOMÍNIO DO CHAT SERVICE É INTENCIONALMENTE MINÚSCULO
//
// Este serviço não é dono de nenhum dado persistente. Ele:
//   - mantém conexões WebSocket abertas;
//   - roteia mensagens em tempo real entre elas;
//   - publica eventos para que outros serviços cuidem da durabilidade.
//
// A propriedade dos dados está no Message Service (mensagens e conversas) e no
// Identity Service (usuários). O Chat Service é deliberadamente efêmero: pode
// ser reiniciado a qualquer momento sem perda de dado — os clientes reconectam e
// o histórico continua íntegro.
//
// É por isso que aqui não há entidade rica, repositório ou banco. Resistir à
// tentação de "dar um banco a cada serviço" é parte do desenho: um serviço sem
// estado escala horizontalmente sem coordenação.
// =============================================================================

/// <summary>
/// Vínculo entre um usuário e uma conexão WebSocket ativa.
/// </summary>
/// <remarks>
/// Um mesmo usuário pode ter várias conexões simultâneas — celular, notebook,
/// duas abas do navegador. É por isso que o registro de conexões usa um conjunto
/// por usuário, e não um único valor.
/// </remarks>
public sealed record ChatConnection(Guid UserId, string ConnectionId);

/// <summary>
/// Participação de um usuário numa conversa.
/// </summary>
/// <remarks>
/// Representa apenas o resultado de uma consulta de autorização; a verdade sobre
/// participação mora no Message Service.
/// </remarks>
public sealed record ConversationMembership(Guid ConversationId, Guid UserId);
