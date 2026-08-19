using IdentityService.Domain;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Infrastructure.Persistence;

/// <summary>
/// Contexto do Entity Framework Core do Identity Service.
/// </summary>
/// <remarks>
/// <para>
/// <b>Banco por serviço.</b> Este contexto aponta para o banco
/// <c>chat_identity</c>, exclusivo deste serviço. Nenhum outro serviço tem
/// permissão de leitura ou escrita ali.
/// </para>
/// <para>
/// É a regra mais importante — e a mais violada — de microsserviços. Um banco
/// compartilhado recria o acoplamento que a separação pretendia eliminar:
/// qualquer alteração de schema vira uma negociação entre times, e a promessa de
/// deploy independente deixa de valer. Quem precisa de dados de usuário os
/// recebe pelo evento <c>UserCreatedEvent</c>, não por um <c>JOIN</c>.
/// </para>
/// </remarks>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    /// <summary>Contas de usuário.</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>Refresh tokens emitidos.</summary>
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>Eventos de integração pendentes de publicação.</summary>
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(builder =>
        {
            builder.ToTable("users");
            builder.HasKey(entity => entity.Id);

            // A chave é gerada pelo DOMÍNIO (`User.Register` faz `Guid.NewGuid()`),
            // não pelo banco. Ver a nota em RefreshToken abaixo para o motivo de
            // isso precisar ser declarado explicitamente.
            builder.Property(entity => entity.Id).ValueGeneratedNever();

            builder.Property(entity => entity.Name).HasMaxLength(128).IsRequired();
            builder.Property(entity => entity.Email).HasMaxLength(256).IsRequired();

            // Índice ÚNICO no e-mail. Cumpre dois papéis:
            //   1. Desempenho: o login busca por e-mail a cada autenticação.
            //   2. Correção: é a única garantia real de unicidade. A checagem
            //      feita no handler (`EmailExistsAsync`) é uma corrida —
            //      dois cadastros simultâneos com o mesmo e-mail passariam os
            //      dois pela verificação antes de qualquer um gravar. O índice é
            //      a rede de segurança que o banco impõe de fato.
            builder.HasIndex(entity => entity.Email).IsUnique();

            builder.Property(entity => entity.PasswordHash).HasMaxLength(256).IsRequired();
            builder.Property(entity => entity.CreatedAtUtc).IsRequired();

            builder.HasMany(entity => entity.RefreshTokens)
                .WithOne()
                .HasForeignKey(token => token.UserId)
                // Cascade: apagar o usuário apaga seus tokens. Deixar tokens
                // órfãos apontando para um usuário inexistente não tem
                // significado algum no domínio.
                .OnDelete(DeleteBehavior.Cascade);

            // Informa ao EF que a coleção é manipulada pelo campo privado, e não
            // pela propriedade somente-leitura. Sem isso o encapsulamento da
            // entidade quebraria o mapeamento.
            builder.Metadata
                .FindNavigation(nameof(User.RefreshTokens))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<RefreshToken>(builder =>
        {
            builder.ToTable("refresh_tokens");
            builder.HasKey(entity => entity.Id);

            // ValueGeneratedNever: A CHAVE É ATRIBUÍDA PELO DOMÍNIO.
            //
            // Sem esta declaração, o EF Core assume `ValueGeneratedOnAdd` para
            // chaves Guid — ou seja, que o valor viria do banco. Ao encontrar uma
            // entidade nova, dentro da coleção de um agregado já rastreado, com a
            // chave JÁ PREENCHIDA, ele conclui que se trata de uma entidade
            // existente e a marca como `Modified` em vez de `Added`.
            //
            // O resultado é um UPDATE numa linha que não existe. O PostgreSQL
            // reporta "0 rows affected" e o EF lança
            // DbUpdateConcurrencyException — que vira 500 para o usuário.
            //
            // O sintoma concreto: a ROTAÇÃO DO REFRESH TOKEN falhava por completo.
            // Nem o token novo era inserido, nem o antigo era revogado. Foi
            // detectado ao exercitar `POST /api/auth/refresh` com a stack real no
            // ar — os testes unitários não pegam isso, porque o defeito está no
            // mapeamento, e não na lógica.
            builder.Property(entity => entity.Id).ValueGeneratedNever();

            builder.Property(entity => entity.Token).HasMaxLength(512).IsRequired();

            // Único porque o token é a chave de busca da renovação e não pode
            // haver colisão entre usuários.
            builder.HasIndex(entity => entity.Token).IsUnique();

            builder.Property(entity => entity.ExpiresAtUtc).IsRequired();
            builder.Property(entity => entity.CreatedAtUtc).IsRequired();

            // Índice de apoio à futura rotina de limpeza de tokens vencidos.
            builder.HasIndex(entity => entity.ExpiresAtUtc);
        });

        modelBuilder.Entity<OutboxMessage>(builder =>
        {
            builder.ToTable("outbox_messages");
            builder.HasKey(entity => entity.Id);

            // A chave reaproveita o EventId do evento de integração.
            builder.Property(entity => entity.Id).ValueGeneratedNever();

            builder.Property(entity => entity.Type).HasMaxLength(256).IsRequired();

            // jsonb (e não text): o PostgreSQL armazena em formato binário
            // estruturado, o que permite consultar dentro do payload em
            // investigações — algo impossível com texto puro.
            builder.Property(entity => entity.Payload).HasColumnType("jsonb").IsRequired();

            builder.Property(entity => entity.OccurredOnUtc).IsRequired();
            builder.Property(entity => entity.Error).HasMaxLength(1000);

            // ÍNDICE CRÍTICO PARA O DESEMPENHO DO DESPACHANTE.
            //
            // O dispatcher roda a cada 5 segundos com a consulta
            // "WHERE ProcessedOnUtc IS NULL ORDER BY OccurredOnUtc". Sem índice,
            // isso é um seq scan na tabela inteira — que só cresce. O índice
            // PARCIAL (com filtro) indexa apenas as linhas pendentes, que são
            // poucas; as milhões de linhas já processadas nem entram nele.
            // Resultado: um índice que permanece pequeno e rápido para sempre.
            builder.HasIndex(entity => entity.OccurredOnUtc)
                .HasFilter("\"ProcessedOnUtc\" IS NULL")
                .HasDatabaseName("ix_outbox_messages_pending");
        });
    }
}
