namespace MessageService.Domain;

// =============================================================================
// LADO DE LEITURA (read model)
//
// Projeções mantidas por consumidores de eventos, moldadas para responder às
// telas do produto com o mínimo de trabalho possível.
//
// POR QUE SEPARAR LEITURA DE ESCRITA (CQRS)
// -----------------------------------------
// As duas cargas de trabalho têm exigências opostas. A escrita quer dados
// normalizados, para que cada fato exista em um único lugar e não haja
// contradição. A leitura quer dados já agregados, para não pagar JOINs a cada
// requisição.
//
// Exemplo concreto deste projeto: a tela de lista de conversas mostra a última
// mensagem de cada conversa. Só com o modelo de escrita, isso exigiria varrer a
// tabela de mensagens e agrupar por conversa a cada abertura da tela. A projeção
// `ConversationReadModel` já guarda a última mensagem pronta — a consulta vira
// uma leitura direta por índice.
//
// O PREÇO: CONSISTÊNCIA EVENTUAL
// ------------------------------
// A projeção é atualizada de forma assíncrona, por um consumidor de evento. Há
// uma janela (tipicamente de milissegundos) em que a mensagem já foi gravada
// mas a lista de conversas ainda mostra a anterior. É um compromisso aceitável
// aqui porque o usuário vê a mensagem chegar em tempo real pelo SignalR — o
// caminho lento é apenas o histórico. Seria inaceitável, por exemplo, num saldo
// bancário.
// =============================================================================

/// <summary>
/// Projeção de leitura de uma mensagem.
/// </summary>
/// <remarks>
/// Hoje é praticamente idêntica à entidade <see cref="Message"/>, e isso é
/// intencional: manter os tipos separados desde o início permite que o read
/// model evolua sem tocar no write model. Ao acrescentar "quantidade de reações"
/// ou "status de leitura", esses campos entram aqui — atualizados por outros
/// eventos — sem contaminar a tabela transacional.
/// </remarks>
public sealed class MessageReadModel
{
    /// <summary>Identificador da mensagem (o mesmo do write model).</summary>
    public Guid Id { get; private set; }

    /// <summary>Conversa à qual pertence.</summary>
    public Guid ConversationId { get; private set; }

    /// <summary>Autor.</summary>
    public Guid SenderId { get; private set; }

    /// <summary>Nome do autor no momento do envio.</summary>
    public string SenderName { get; private set; } = string.Empty;

    /// <summary>Conteúdo textual.</summary>
    public string Content { get; private set; } = string.Empty;

    /// <summary>Instante de envio, em UTC.</summary>
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>Cria a projeção.</summary>
    public static MessageReadModel Create(
        Guid id, Guid conversationId, Guid senderId, string senderName, string content, DateTime createdAtUtc)
    {
        return new MessageReadModel
        {
            Id = id,
            ConversationId = conversationId,
            SenderId = senderId,
            SenderName = senderName,
            Content = content,
            CreatedAtUtc = createdAtUtc
        };
    }
}

/// <summary>
/// Projeção que mantém o resumo de uma conversa para a listagem lateral.
/// </summary>
public sealed class ConversationReadModel
{
    /// <summary>Identificador da conversa.</summary>
    public Guid Id { get; private set; }

    /// <summary>Prévia da última mensagem.</summary>
    public string LastMessage { get; private set; } = string.Empty;

    /// <summary>Instante da última mensagem; <c>null</c> em conversa sem mensagens.</summary>
    /// <remarks>
    /// É a chave de ordenação da lista: conversas com atividade mais recente
    /// aparecem no topo, como em qualquer aplicativo de mensagens.
    /// </remarks>
    public DateTime? LastMessageAtUtc { get; private set; }

    /// <summary>
    /// Atualiza o resumo com uma mensagem mais recente.
    /// </summary>
    /// <remarks>
    /// <b>Guarda contra evento fora de ordem.</b> A entrega do RabbitMQ não
    /// garante ordenação global, e o retry pode reentregar um evento antigo
    /// depois de um novo. Sem esta comparação, uma mensagem antiga reprocessada
    /// sobrescreveria a prévia atual e a lista mostraria conteúdo desatualizado —
    /// um bug intermitente e muito difícil de reproduzir.
    /// </remarks>
    public void Update(string lastMessage, DateTime lastMessageAtUtc)
    {
        if (LastMessageAtUtc is not null && lastMessageAtUtc <= LastMessageAtUtc)
        {
            return;
        }

        LastMessage = lastMessage;
        LastMessageAtUtc = lastMessageAtUtc;
    }

    /// <summary>Cria a projeção da conversa.</summary>
    public static ConversationReadModel Create(Guid id, string lastMessage = "", DateTime? lastMessageAtUtc = null)
    {
        return new ConversationReadModel
        {
            Id = id,
            LastMessage = lastMessage,
            LastMessageAtUtc = lastMessageAtUtc
        };
    }
}

/// <summary>
/// Projeção do vínculo usuário–conversa, usada para responder "quais conversas
/// este usuário tem?" sem JOIN com o modelo de escrita.
/// </summary>
public sealed class ConversationParticipantReadModel
{
    /// <summary>Conversa.</summary>
    public Guid ConversationId { get; private set; }

    /// <summary>Usuário participante.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Cria a projeção do vínculo.</summary>
    public static ConversationParticipantReadModel Create(Guid conversationId, Guid userId)
    {
        return new ConversationParticipantReadModel
        {
            ConversationId = conversationId,
            UserId = userId
        };
    }
}
