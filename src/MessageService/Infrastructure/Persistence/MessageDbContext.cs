using MessageService.Domain;
using Microsoft.EntityFrameworkCore;

namespace MessageService.Infrastructure.Persistence;

/// <summary>
/// Contexto do EF Core do Message Service, abrangendo os modelos de escrita e de
/// leitura.
/// </summary>
/// <remarks>
/// <para>
/// <b>Escrita e leitura no mesmo banco: uma decisão consciente.</b> A literatura
/// de CQRS costuma mostrar bancos fisicamente separados. Aqui os dois modelos
/// vivem no mesmo PostgreSQL, em tabelas distintas, porque nesta escala a
/// separação física traria complexidade operacional (dois bancos para provisionar,
/// monitorar e sincronizar) sem benefício mensurável.
/// </para>
/// <para>
/// O que já se ganha com a separação lógica: índices otimizados por carga de
/// trabalho, projeções reconstruíveis de forma independente, e um caminho de
/// migração pronto — mover os read models para uma réplica de leitura, ou para
/// um banco especializado, não exige tocar na camada de aplicação.
/// </para>
/// </remarks>
public sealed class MessageDbContext(DbContextOptions<MessageDbContext> options) : DbContext(options)
{
    // ---- Modelo de escrita (fonte de verdade) ----

    /// <summary>Conversas.</summary>
    public DbSet<Conversation> Conversations => Set<Conversation>();

    /// <summary>Mensagens persistidas.</summary>
    public DbSet<Message> Messages => Set<Message>();

    /// <summary>Vínculos usuário–conversa (base da autorização).</summary>
    public DbSet<ConversationParticipant> ConversationParticipants => Set<ConversationParticipant>();

    // ---- Modelo de leitura (projeções) ----

    /// <summary>Projeção de mensagens.</summary>
    public DbSet<MessageReadModel> MessageReadModels => Set<MessageReadModel>();

    /// <summary>Projeção de resumos de conversa.</summary>
    public DbSet<ConversationReadModel> ConversationReadModels => Set<ConversationReadModel>();

    /// <summary>Projeção de participantes.</summary>
    public DbSet<ConversationParticipantReadModel> ConversationParticipantReadModels =>
        Set<ConversationParticipantReadModel>();

    // ---- Mensageria confiável ----

    /// <summary>Eventos pendentes de publicação.</summary>
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>Eventos já processados (deduplicação).</summary>
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureWriteModel(modelBuilder);
        ConfigureReadModel(modelBuilder);
        ConfigureMessagingTables(modelBuilder);
    }

    private static void ConfigureWriteModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Conversation>(builder =>
        {
            builder.ToTable("conversations");
            builder.HasKey(entity => entity.Id);

            // Todas as chaves deste serviço são atribuídas pelo domínio ou vêm
            // dentro de um evento de integração — nunca são geradas pelo banco.
            // Declarar isso evita que o EF classifique entidades novas como
            // `Modified` (ver a nota detalhada em IdentityDbContext).
            builder.Property(entity => entity.Id).ValueGeneratedNever();
            builder.Property(entity => entity.CreatedAtUtc).IsRequired();
        });

        modelBuilder.Entity<Message>(builder =>
        {
            builder.ToTable("messages");
            builder.HasKey(entity => entity.Id);
            builder.Property(entity => entity.Id).ValueGeneratedNever();

            builder.Property(entity => entity.SenderName).HasMaxLength(256).IsRequired();
            builder.Property(entity => entity.Content).HasMaxLength(4000).IsRequired();

            // Índice COMPOSTO (conversa, data), e não apenas por conversa.
            //
            // A consulta de histórico é sempre "mensagens da conversa X ordenadas
            // por data". Com índice só em ConversationId, o PostgreSQL encontra
            // as linhas rapidamente mas ainda precisa ordená-las em memória — o
            // que degrada conforme a conversa cresce. Com o índice composto, os
            // dados já saem do índice na ordem certa e o passo de ordenação some
            // do plano de execução.
            builder.HasIndex(entity => new { entity.ConversationId, entity.CreatedAtUtc })
                .HasDatabaseName("ix_messages_conversation_created");
        });

        modelBuilder.Entity<ConversationParticipant>(builder =>
        {
            builder.ToTable("conversation_participants");

            // Chave composta: um usuário participa de uma conversa uma única vez.
            // A unicidade é imposta pelo banco, e não por checagem no código —
            // que estaria sujeita a corrida entre requisições concorrentes.
            builder.HasKey(entity => new { entity.ConversationId, entity.UserId });

            // Índice na direção inversa da chave primária.
            //
            // A chave primária serve à pergunta "quem participa da conversa X?".
            // Este índice serve à pergunta oposta — "de quais conversas o usuário
            // Y participa?" — que é exatamente a da tela de lista de conversas.
            // Sem ele, essa consulta faria varredura completa da tabela.
            builder.HasIndex(entity => entity.UserId)
                .HasDatabaseName("ix_conversation_participants_user");
        });
    }

    private static void ConfigureReadModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MessageReadModel>(builder =>
        {
            builder.ToTable("message_read_models");
            builder.HasKey(entity => entity.Id);
            builder.Property(entity => entity.Id).ValueGeneratedNever();

            builder.Property(entity => entity.Content).HasMaxLength(4000).IsRequired();
            builder.Property(entity => entity.SenderName).HasMaxLength(256).IsRequired();

            builder.HasIndex(entity => new { entity.ConversationId, entity.CreatedAtUtc })
                .HasDatabaseName("ix_message_read_models_conversation_created");
        });

        modelBuilder.Entity<ConversationReadModel>(builder =>
        {
            builder.ToTable("conversation_read_models");
            builder.HasKey(entity => entity.Id);
            builder.Property(entity => entity.Id).ValueGeneratedNever();
            builder.Property(entity => entity.LastMessage).HasMaxLength(4000).IsRequired();
        });

        modelBuilder.Entity<ConversationParticipantReadModel>(builder =>
        {
            builder.ToTable("conversation_participant_read_models");
            builder.HasKey(entity => new { entity.ConversationId, entity.UserId });

            builder.HasIndex(entity => entity.UserId)
                .HasDatabaseName("ix_conversation_participant_read_models_user");
        });
    }

    private static void ConfigureMessagingTables(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(builder =>
        {
            builder.ToTable("outbox_messages");
            builder.HasKey(entity => entity.Id);
            builder.Property(entity => entity.Id).ValueGeneratedNever();

            builder.Property(entity => entity.Type).HasMaxLength(256).IsRequired();
            builder.Property(entity => entity.Payload).HasColumnType("jsonb").IsRequired();
            builder.Property(entity => entity.OccurredOnUtc).IsRequired();
            builder.Property(entity => entity.Error).HasMaxLength(1000);

            // Índice parcial: só as linhas pendentes entram, mantendo-o pequeno
            // mesmo depois de milhões de eventos processados.
            builder.HasIndex(entity => entity.OccurredOnUtc)
                .HasFilter("\"ProcessedOnUtc\" IS NULL")
                .HasDatabaseName("ix_message_outbox_pending");
        });

        modelBuilder.Entity<InboxMessage>(builder =>
        {
            builder.ToTable("inbox_messages");

            // (EventId, ConsumerName): o mesmo evento pode ser processado por
            // consumidores diferentes, cada um com seu próprio registro.
            builder.HasKey(entity => new { entity.EventId, entity.ConsumerName });

            builder.Property(entity => entity.ConsumerName).HasMaxLength(256).IsRequired();
            builder.Property(entity => entity.ProcessedAtUtc).IsRequired();

            // Apoia a futura rotina de expurgo de registros antigos.
            builder.HasIndex(entity => entity.ProcessedAtUtc)
                .HasDatabaseName("ix_inbox_messages_processed_at");
        });
    }
}
