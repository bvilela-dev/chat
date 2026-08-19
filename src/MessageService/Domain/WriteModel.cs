namespace MessageService.Domain;

// =============================================================================
// LADO DE ESCRITA (write model)
//
// Estas entidades são a fonte de verdade. São gravadas normalizadas, priorizando
// consistência e integridade referencial — não velocidade de leitura.
// As entidades otimizadas para consulta ficam em ReadModel.cs.
// =============================================================================

/// <summary>
/// Uma conversa: direta (dois participantes) ou em grupo.
/// </summary>
public sealed class Conversation
{
    /// <summary>Construtor exigido pelo EF Core.</summary>
    private Conversation()
    {
    }

    private Conversation(Guid id, bool isGroup, DateTime createdAtUtc)
    {
        Id = id;
        IsGroup = isGroup;
        CreatedAtUtc = createdAtUtc;
    }

    /// <summary>Identificador da conversa.</summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Distingue conversa em grupo de conversa direta.
    /// </summary>
    /// <remarks>
    /// A diferença não é só de rótulo: conversa direta tem no máximo dois
    /// participantes e é deduplicada por par de usuários (abrir a conversa com a
    /// mesma pessoa duas vezes deve levar à mesma conversa). Grupo não tem
    /// nenhuma dessas restrições.
    /// </remarks>
    public bool IsGroup { get; private set; }

    /// <summary>Instante de criação, em UTC.</summary>
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>Cria uma conversa com identificador conhecido.</summary>
    /// <remarks>
    /// O identificador vem de fora (e não é gerado aqui) porque a conversa pode
    /// ser criada de duas origens: pelo comando explícito do usuário, ou de forma
    /// implícita ao processar uma mensagem cuja conversa ainda não existia no
    /// banco. Nos dois casos o identificador já foi definido antes.
    /// </remarks>
    public static Conversation Create(Guid id, bool isGroup, DateTime createdAtUtc)
    {
        return new Conversation(id, isGroup, createdAtUtc);
    }
}

/// <summary>
/// Uma mensagem persistida.
/// </summary>
/// <remarks>
/// <b>Imutável por natureza.</b> Não há método para alterar o conteúdo. Uma
/// mensagem é o registro de um fato que ocorreu; editá-la reescreveria a
/// história. Se o produto vier a suportar edição, o modelo correto é uma nova
/// entidade de revisão apontando para a original, preservando o histórico.
/// </remarks>
public sealed class Message
{
    /// <summary>Construtor exigido pelo EF Core.</summary>
    private Message()
    {
    }

    private Message(Guid id, Guid conversationId, Guid senderId, string senderName, string content, DateTime createdAtUtc)
    {
        Id = id;
        ConversationId = conversationId;
        SenderId = senderId;
        SenderName = senderName;
        Content = content;
        CreatedAtUtc = createdAtUtc;
    }

    /// <summary>Identificador da mensagem.</summary>
    public Guid Id { get; private set; }

    /// <summary>Conversa à qual a mensagem pertence.</summary>
    public Guid ConversationId { get; private set; }

    /// <summary>Autor da mensagem.</summary>
    public Guid SenderId { get; private set; }

    /// <summary>
    /// Nome do autor no momento do envio.
    /// </summary>
    /// <remarks>
    /// Desnormalização deliberada. O nome é copiado para cá em vez de ser buscado
    /// no Identity Service a cada leitura, por dois motivos: (1) exibir o
    /// histórico não pode depender de outro serviço estar no ar; (2) é
    /// semanticamente correto — a mensagem foi assinada com aquele nome, e uma
    /// troca de nome posterior não deveria reescrever mensagens antigas.
    /// </remarks>
    public string SenderName { get; private set; } = string.Empty;

    /// <summary>Conteúdo textual.</summary>
    public string Content { get; private set; } = string.Empty;

    /// <summary>Instante de envio, em UTC.</summary>
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>Cria uma mensagem.</summary>
    /// <remarks>
    /// O identificador é gerado pelo Chat Service no instante do envio e viaja
    /// no evento de integração. Isso é o que permite ao consumidor detectar
    /// reprocessamento: se já existe mensagem com aquele id, o evento é duplicado.
    /// </remarks>
    public static Message Create(Guid id, Guid conversationId, Guid senderId, string senderName, string content, DateTime createdAtUtc)
    {
        return new Message(id, conversationId, senderId, senderName, content, createdAtUtc);
    }
}

/// <summary>
/// Vínculo entre um usuário e uma conversa.
/// </summary>
/// <remarks>
/// <b>É a tabela que sustenta a autorização.</b> Toda pergunta do tipo "este
/// usuário pode ler esta conversa?" é respondida consultando aqui. Antes das
/// correções deste projeto, essa pergunta simplesmente não era feita — qualquer
/// usuário autenticado conseguia ler o histórico de qualquer conversa apenas
/// informando o identificador na URL.
/// </remarks>
public sealed class ConversationParticipant
{
    /// <summary>Conversa.</summary>
    public Guid ConversationId { get; private set; }

    /// <summary>Usuário participante.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Instante de entrada, em UTC.</summary>
    public DateTime JoinedAtUtc { get; private set; }

    /// <summary>Cria o vínculo.</summary>
    public static ConversationParticipant Create(Guid conversationId, Guid userId, DateTime joinedAtUtc)
    {
        return new ConversationParticipant
        {
            ConversationId = conversationId,
            UserId = userId,
            JoinedAtUtc = joinedAtUtc
        };
    }
}
