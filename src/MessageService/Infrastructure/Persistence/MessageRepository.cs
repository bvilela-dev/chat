using System.Text.Json;
using BuildingBlocks.Contracts;
using MessageService.Application.Abstractions;
using MessageService.Application.Contracts;
using MessageService.Domain;
using Microsoft.EntityFrameworkCore;

namespace MessageService.Infrastructure.Persistence;

/// <summary>
/// Implementação do <see cref="IMessageRepository"/> sobre o EF Core.
/// </summary>
public sealed class MessageRepository(MessageDbContext dbContext) : IMessageRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    // =========================================================================
    // Idempotência (inbox)
    // =========================================================================

    /// <inheritdoc />
    public Task<bool> HasProcessedAsync(Guid eventId, string consumerName, CancellationToken cancellationToken)
    {
        return dbContext.InboxMessages.AnyAsync(
            message => message.EventId == eventId && message.ConsumerName == consumerName,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task MarkProcessedAsync(
        Guid eventId,
        string consumerName,
        DateTime processedAtUtc,
        CancellationToken cancellationToken)
    {
        // A checagem evita a violação de chave primária no caso — raro, mas real
        // — de a mesma mensagem ser processada duas vezes dentro da janela de um
        // único SaveChanges.
        if (await HasProcessedAsync(eventId, consumerName, cancellationToken))
        {
            return;
        }

        // O instante vem por parâmetro, do IClock do chamador.
        //
        // Correção em relação à versão anterior, que chamava `DateTime.UtcNow`
        // direto aqui dentro — furando a abstração de relógio e tornando este
        // método impossível de testar de forma determinística.
        await dbContext.InboxMessages.AddAsync(
            InboxMessage.Create(eventId, consumerName, processedAtUtc),
            cancellationToken);
    }

    // =========================================================================
    // Autorização
    // =========================================================================

    /// <inheritdoc />
    public Task<bool> IsParticipantAsync(Guid conversationId, Guid userId, CancellationToken cancellationToken)
    {
        // Consulta o modelo de ESCRITA, e não a projeção de leitura.
        //
        // A escolha é deliberada: decisão de autorização não pode se apoiar em
        // dado eventualmente consistente. Uma projeção atrasada em alguns
        // milissegundos é irrelevante para exibir uma lista, mas em autorização
        // significaria negar acesso a quem acabou de entrar na conversa — ou,
        // pior, conceder a quem acabou de sair.
        //
        // A consulta é barata: usa a chave primária composta
        // (ConversationId, UserId), então é um único index seek.
        return dbContext.ConversationParticipants.AnyAsync(
            participant => participant.ConversationId == conversationId && participant.UserId == userId,
            cancellationToken);
    }

    // =========================================================================
    // Escrita
    // =========================================================================

    /// <inheritdoc />
    public Task<bool> MessageExistsAsync(Guid messageId, CancellationToken cancellationToken)
    {
        return dbContext.Messages.AnyAsync(message => message.Id == messageId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Conversation?> GetConversationAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        return dbContext.Conversations.SingleOrDefaultAsync(
            conversation => conversation.Id == conversationId,
            cancellationToken);
    }

    /// <summary>
    /// Localiza a conversa direta entre dois usuários.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Esta consulta foi reescrita.</b> A versão anterior tinha cerca de 60
    /// linhas de LINQ com duas estratégias encadeadas: primeiro procurava pela
    /// tabela de participantes; se não achasse, executava uma segunda consulta de
    /// "recuperação" que tentava inferir os participantes cruzando a tabela de
    /// participantes, a projeção de participantes <b>e</b> os remetentes das
    /// mensagens, com seis subconsultas correlacionadas e três negações.
    /// </para>
    /// <para>
    /// Aquela heurística existia para contornar um sintoma: conversas cujos
    /// vínculos de participação nunca foram gravados. A causa raiz estava no
    /// fluxo de persistência — que registrava apenas o remetente — e foi corrigida
    /// em <c>PersistMessageCommandHandler</c> e em
    /// <c>CreateDirectConversationCommandHandler</c>, que agora garantem os dois
    /// vínculos. Com a origem resolvida, o contorno deixou de ser necessário.
    /// </para>
    /// <para>
    /// O ganho é concreto: a consulta ficou compreensível, o plano de execução
    /// passa a usar o índice de participantes em vez de varrer três tabelas, e o
    /// comportamento ficou previsível — a versão antiga podia, em cenários de
    /// borda, casar uma conversa em grupo esvaziada como se fosse uma conversa
    /// direta.
    /// </para>
    /// </remarks>
    public async Task<ConversationSummary?> GetDirectConversationAsync(
        Guid firstUserId,
        Guid secondUserId,
        CancellationToken cancellationToken)
    {
        // Uma conversa direta é aquela que: não é grupo, tem exatamente os dois
        // usuários informados como participantes, e mais ninguém.
        var conversationId = await dbContext.Conversations
            .Where(conversation => !conversation.IsGroup)
            .Where(conversation => dbContext.ConversationParticipants
                .Count(participant => participant.ConversationId == conversation.Id) == 2)
            .Where(conversation => dbContext.ConversationParticipants
                .Any(participant => participant.ConversationId == conversation.Id && participant.UserId == firstUserId))
            .Where(conversation => dbContext.ConversationParticipants
                .Any(participant => participant.ConversationId == conversation.Id && participant.UserId == secondUserId))
            .Select(conversation => (Guid?)conversation.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (conversationId is null)
        {
            return null;
        }

        // Busca o resumo em consulta separada. Combinar tudo numa única query
        // produziria subconsultas correlacionadas no SELECT — que o PostgreSQL
        // executa uma vez por linha do resultado. Duas consultas simples e
        // indexadas custam menos que uma complexa.
        var readModel = await dbContext.ConversationReadModels
            .AsNoTracking()
            .Where(model => model.Id == conversationId)
            .Select(model => new { model.LastMessage, model.LastMessageAtUtc })
            .FirstOrDefaultAsync(cancellationToken);

        return new ConversationSummary(
            conversationId.Value,
            readModel?.LastMessage ?? string.Empty,
            readModel?.LastMessageAtUtc,
            IsGroup: false,
            CounterpartUserId: secondUserId);
    }

    /// <inheritdoc />
    public Task AddConversationAsync(Conversation conversation, CancellationToken cancellationToken)
    {
        return dbContext.Conversations.AddAsync(conversation, cancellationToken).AsTask();
    }

    /// <inheritdoc />
    public async Task CreateDirectConversationAsync(
        Guid conversationId,
        Guid initiatorId,
        Guid participantId,
        DateTime createdAtUtc,
        CancellationToken cancellationToken)
    {
        await dbContext.Conversations.AddAsync(
            Conversation.Create(conversationId, isGroup: false, createdAtUtc),
            cancellationToken);

        // Grava o vínculo nos DOIS modelos, de escrita e de leitura.
        //
        // Diferentemente das mensagens — cuja projeção é assíncrona —, a
        // participação é projetada de forma síncrona. O motivo é de experiência
        // de uso: a conversa precisa aparecer na lista imediatamente após ser
        // criada. Um atraso aqui faria o usuário clicar num contato e não ver
        // nada acontecer.
        await dbContext.ConversationParticipants.AddRangeAsync(
            [
                ConversationParticipant.Create(conversationId, initiatorId, createdAtUtc),
                ConversationParticipant.Create(conversationId, participantId, createdAtUtc)
            ],
            cancellationToken);

        await dbContext.ConversationParticipantReadModels.AddRangeAsync(
            [
                ConversationParticipantReadModel.Create(conversationId, initiatorId),
                ConversationParticipantReadModel.Create(conversationId, participantId)
            ],
            cancellationToken);

        await dbContext.ConversationReadModels.AddAsync(
            ConversationReadModel.Create(conversationId),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task AddParticipantAsync(
        Guid conversationId,
        Guid userId,
        DateTime joinedAtUtc,
        CancellationToken cancellationToken)
    {
        // Idempotente: chamar repetidamente não duplica nem lança exceção. É o
        // que permite reprocessar um evento de entrada sem efeito colateral.
        if (!await IsParticipantAsync(conversationId, userId, cancellationToken))
        {
            await dbContext.ConversationParticipants.AddAsync(
                ConversationParticipant.Create(conversationId, userId, joinedAtUtc),
                cancellationToken);
        }

        var hasReadModel = await dbContext.ConversationParticipantReadModels.AnyAsync(
            participant => participant.ConversationId == conversationId && participant.UserId == userId,
            cancellationToken);

        if (!hasReadModel)
        {
            await dbContext.ConversationParticipantReadModels.AddAsync(
                ConversationParticipantReadModel.Create(conversationId, userId),
                cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task RemoveParticipantAsync(Guid conversationId, Guid userId, CancellationToken cancellationToken)
    {
        var participant = await dbContext.ConversationParticipants.SingleOrDefaultAsync(
            entity => entity.ConversationId == conversationId && entity.UserId == userId,
            cancellationToken);

        if (participant is not null)
        {
            dbContext.ConversationParticipants.Remove(participant);
        }

        var readModelParticipant = await dbContext.ConversationParticipantReadModels.SingleOrDefaultAsync(
            entity => entity.ConversationId == conversationId && entity.UserId == userId,
            cancellationToken);

        if (readModelParticipant is not null)
        {
            dbContext.ConversationParticipantReadModels.Remove(readModelParticipant);
        }
    }

    /// <inheritdoc />
    public void AddMessage(Message message)
    {
        dbContext.Messages.Add(message);
    }

    /// <inheritdoc />
    public void EnqueueOutbox(IIntegrationEvent integrationEvent)
    {
        var eventType = integrationEvent.GetType();

        dbContext.OutboxMessages.Add(OutboxMessage.Create(
            integrationEvent.EventId,
            eventType.Name,
            // Serializa pelo tipo concreto: passar a interface faria o
            // serializador emitir um objeto vazio, sem nenhum aviso.
            JsonSerializer.Serialize(integrationEvent, eventType, SerializerOptions),
            integrationEvent.OccurredAtUtc));
    }

    // =========================================================================
    // Projeções e leitura
    // =========================================================================

    /// <inheritdoc />
    public async Task UpsertProjectionAsync(MessageProjectionRequested projection, CancellationToken cancellationToken)
    {
        var messageAlreadyProjected = await dbContext.MessageReadModels.AnyAsync(
            entity => entity.Id == projection.MessageId,
            cancellationToken);

        if (!messageAlreadyProjected)
        {
            await dbContext.MessageReadModels.AddAsync(
                MessageReadModel.Create(
                    projection.MessageId,
                    projection.ConversationId,
                    projection.SenderId,
                    projection.SenderName,
                    projection.Content,
                    projection.MessageCreatedAtUtc),
                cancellationToken);
        }

        var conversationReadModel = await dbContext.ConversationReadModels.SingleOrDefaultAsync(
            entity => entity.Id == projection.ConversationId,
            cancellationToken);

        if (conversationReadModel is null)
        {
            await dbContext.ConversationReadModels.AddAsync(
                ConversationReadModel.Create(
                    projection.ConversationId,
                    projection.Content,
                    projection.MessageCreatedAtUtc),
                cancellationToken);
        }
        else
        {
            // A própria entidade descarta atualizações mais antigas que a atual
            // (proteção contra evento fora de ordem). A regra vive no domínio,
            // não aqui.
            conversationReadModel.Update(projection.Content, projection.MessageCreatedAtUtc);
        }

        var senderHasReadModel = await dbContext.ConversationParticipantReadModels.AnyAsync(
            entity => entity.ConversationId == projection.ConversationId && entity.UserId == projection.SenderId,
            cancellationToken);

        if (!senderHasReadModel)
        {
            await dbContext.ConversationParticipantReadModels.AddAsync(
                ConversationParticipantReadModel.Create(projection.ConversationId, projection.SenderId),
                cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<MessageReadModel>> GetMessagesByConversationAsync(
        Guid conversationId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        // Paginação por OFFSET (Skip/Take).
        //
        // Limitação conhecida: o PostgreSQL precisa varrer e descartar as linhas
        // puladas, então a página 1.000 é bem mais cara que a página 1. Para
        // histórico de chat, a paginação por cursor ("mensagens anteriores a este
        // instante") seria melhor — desempenho constante e imune ao deslocamento
        // causado por mensagens novas chegando durante a navegação. Fica anotado
        // como próximo passo; o teto de 200 itens por página limita o dano hoje.
        return await dbContext.MessageReadModels
            .AsNoTracking()
            .Where(entity => entity.ConversationId == conversationId)
            .OrderBy(entity => entity.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<ConversationSummary>> GetUserConversationsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        // Parte das conversas em que o usuário participa (usa o índice por
        // UserId) e junta as projeções necessárias.
        var conversations = await dbContext.ConversationParticipantReadModels
            .AsNoTracking()
            .Where(participant => participant.UserId == userId)
            .Join(
                dbContext.Conversations,
                participant => participant.ConversationId,
                conversation => conversation.Id,
                (participant, conversation) => conversation)
            .Select(conversation => new
            {
                conversation.Id,
                conversation.IsGroup,
                ReadModel = dbContext.ConversationReadModels
                    .Where(model => model.Id == conversation.Id)
                    .Select(model => new { model.LastMessage, model.LastMessageAtUtc })
                    .FirstOrDefault(),
                CounterpartUserId = conversation.IsGroup
                    ? null
                    : dbContext.ConversationParticipantReadModels
                        .Where(other => other.ConversationId == conversation.Id && other.UserId != userId)
                        .Select(other => (Guid?)other.UserId)
                        .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        // A ordenação final acontece em memória porque a lista de conversas de um
        // usuário é pequena (dezenas, não milhares) e porque `LastMessageAtUtc`
        // é anulável — o tratamento de nulos na ordenação fica mais legível aqui
        // do que traduzido para SQL.
        return
        [
            .. conversations
                .Select(item => new ConversationSummary(
                    item.Id,
                    item.ReadModel?.LastMessage ?? string.Empty,
                    item.ReadModel?.LastMessageAtUtc,
                    item.IsGroup,
                    item.CounterpartUserId))
                .OrderByDescending(summary => summary.LastMessageAtUtc ?? DateTime.MinValue)
        ];
    }

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
