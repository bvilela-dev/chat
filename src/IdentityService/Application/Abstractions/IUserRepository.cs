using IdentityService.Domain;

namespace IdentityService.Application.Abstractions;

/// <summary>
/// Porta de acesso ao armazenamento de usuários.
/// </summary>
/// <remarks>
/// <para>
/// Esta interface é declarada na camada de <b>Aplicação</b> e implementada na
/// camada de <b>Infraestrutura</b>. Essa inversão é o que a Clean Architecture
/// chama de <i>Dependency Inversion</i>, e é o que mantém a regra de dependência
/// apontando para dentro:
/// </para>
/// <code>
/// API ──► Infrastructure ──► Application ──► Domain
///                    │                ▲
///                    └── implementa ──┘  (a seta de dependência aponta
///                                          para o centro, sempre)
/// </code>
/// <para>
/// A consequência prática: a lógica de "cadastrar usuário" não sabe que existe
/// PostgreSQL. Trocar o banco, ou testar o handler com um repositório em
/// memória, não exige tocar em uma linha da regra de negócio.
/// </para>
/// </remarks>
public interface IUserRepository
{
    /// <summary>Indica se já existe usuário com o e-mail informado (já normalizado).</summary>
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken);

    /// <summary>Busca um usuário pelo e-mail, carregando seus refresh tokens.</summary>
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken);

    /// <summary>Busca um usuário pelo identificador.</summary>
    Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Busca o usuário dono de um refresh token.</summary>
    Task<User?> GetByRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken);

    /// <summary>Lista todos os usuários, ordenados por nome.</summary>
    Task<IReadOnlyCollection<User>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>Marca um novo usuário para inclusão.</summary>
    Task AddAsync(User user, CancellationToken cancellationToken);

    /// <summary>
    /// Marca explicitamente um refresh token recém-emitido para inclusão.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Poderia ser dispensável: o token já foi acrescentado à coleção do agregado
    /// por <see cref="User.IssueRefreshToken"/>, e a detecção de mudanças do EF
    /// deveria percebê-lo.
    /// </para>
    /// <para>
    /// A chamada explícita existe para tornar a intenção inequívoca. Depender da
    /// inferência do EF sobre entidades novas dentro de uma coleção de navegação
    /// é justamente o que produziu um bug real neste projeto (ver a nota sobre
    /// <c>ValueGeneratedNever</c> em <c>IdentityDbContext</c>): a rotação de
    /// refresh token gerava UPDATE em vez de INSERT e falhava com 500.
    /// </para>
    /// <para>
    /// "Explícito é melhor que implícito" vale especialmente quando o
    /// comportamento implícito depende de convenções internas de um ORM.
    /// </para>
    /// </remarks>
    void AddRefreshToken(RefreshToken refreshToken);

    /// <summary>
    /// Confirma no armazenamento todas as alterações pendentes.
    /// </summary>
    /// <remarks>
    /// A separação entre "alterar" e "salvar" é intencional: ela permite que o
    /// handler agrupe várias operações numa <b>única transação</b>. É esse
    /// detalhe que faz o padrão Outbox funcionar — gravar o usuário e enfileirar
    /// o evento precisam ser atômicos (ver <see cref="IOutboxWriter"/>).
    /// </remarks>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
