using IdentityService.Application.Abstractions;
using IdentityService.Domain;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Infrastructure.Persistence;

/// <summary>
/// Implementação do <see cref="IUserRepository"/> sobre o EF Core.
/// </summary>
/// <remarks>
/// O <see cref="IdentityDbContext"/> atua como Unit of Work: as alterações
/// ficam acumuladas no change tracker e só são enviadas ao banco no
/// <see cref="SaveChangesAsync"/>, dentro de uma única transação implícita.
/// </remarks>
public sealed class UserRepository(IdentityDbContext dbContext) : IUserRepository
{
    /// <inheritdoc />
    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken)
    {
        // AnyAsync traduz para EXISTS no SQL, que o PostgreSQL interrompe assim
        // que encontra a primeira linha. Bem mais barato que buscar a entidade
        // inteira só para conferir se ela existe.
        return dbContext.Users.AnyAsync(user => user.Email == email, cancellationToken);
    }

    /// <inheritdoc />
    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken)
    {
        // Include: o login precisa acrescentar um refresh token à coleção, então
        // ela tem de vir carregada e rastreada.
        return dbContext.Users
            .Include(user => user.RefreshTokens)
            .SingleOrDefaultAsync(user => user.Email == email, cancellationToken);
    }

    /// <inheritdoc />
    public Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        // AsNoTracking: esta consulta serve a leituras (perfil, validação gRPC)
        // que não alteram nada. Sem rastreamento, o EF pula a criação de
        // snapshots de mudança — menos alocação e menos trabalho por requisição.
        return dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<User?> GetByRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        return dbContext.Users
            .Include(user => user.RefreshTokens)
            .SingleOrDefaultAsync(
                user => user.RefreshTokens.Any(token => token.Token == refreshToken),
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<User>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Users
            .AsNoTracking()
            .OrderBy(user => user.Name)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task AddAsync(User user, CancellationToken cancellationToken)
    {
        return dbContext.Users.AddAsync(user, cancellationToken).AsTask();
    }

    /// <inheritdoc />
    public void AddRefreshToken(RefreshToken refreshToken)
    {
        dbContext.RefreshTokens.Add(refreshToken);
    }

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
