namespace IdentityService.Domain;

/// <summary>
/// Credencial de longa duração que permite renovar o access token sem que o
/// usuário digite a senha novamente.
/// </summary>
/// <remarks>
/// <para>
/// Diferente do JWT, este token é <b>opaco</b>: não carrega informação nenhuma,
/// é apenas um valor aleatório que serve de chave para uma linha desta tabela.
/// Essa escolha é o que o torna revogável — a validade não está no token, está
/// no banco, e o banco pode mudar de ideia a qualquer momento.
/// </para>
/// <para>
/// É também parte do agregado <see cref="User"/>: não deve ser criado nem
/// consultado fora dele.
/// </para>
/// </remarks>
public sealed class RefreshToken
{
    /// <summary>Construtor exigido pelo EF Core.</summary>
    private RefreshToken()
    {
    }

    private RefreshToken(Guid id, Guid userId, string token, DateTime expiresAtUtc, DateTime createdAtUtc)
    {
        Id = id;
        UserId = userId;
        Token = token;
        ExpiresAtUtc = expiresAtUtc;
        CreatedAtUtc = createdAtUtc;
    }

    /// <summary>Chave primária.</summary>
    public Guid Id { get; private set; }

    /// <summary>Usuário dono do token.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Valor opaco apresentado pelo cliente.</summary>
    public string Token { get; private set; } = string.Empty;

    /// <summary>Instante de expiração, em UTC.</summary>
    public DateTime ExpiresAtUtc { get; private set; }

    /// <summary>Instante de emissão, em UTC.</summary>
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>Indica se o token foi invalidado antes de expirar.</summary>
    public bool IsRevoked { get; private set; }

    /// <summary>Instante da revogação, quando houver.</summary>
    /// <remarks>
    /// Guardar <i>quando</i> foi revogado, e não só <i>que</i> foi, é o que
    /// permite investigar um incidente depois: dá para reconstruir a linha do
    /// tempo de uma sessão suspeita.
    /// </remarks>
    public DateTime? RevokedAtUtc { get; private set; }

    /// <summary>Cria um token vinculado a um usuário.</summary>
    internal static RefreshToken Create(Guid userId, string token, DateTime expiresAtUtc, DateTime createdAtUtc)
    {
        return new RefreshToken(Guid.NewGuid(), userId, token, expiresAtUtc, createdAtUtc);
    }

    /// <summary>
    /// Indica se o token pode ser usado no instante informado.
    /// </summary>
    /// <remarks>
    /// Recebe o "agora" como parâmetro em vez de consultar <c>DateTime.UtcNow</c>
    /// internamente. Isso mantém a entidade livre de dependência do relógio e
    /// permite testar a expiração diretamente, sem qualquer infraestrutura.
    /// </remarks>
    public bool IsActive(DateTime utcNow)
    {
        return !IsRevoked && ExpiresAtUtc > utcNow;
    }

    /// <summary>
    /// Invalida o token imediatamente.
    /// </summary>
    /// <remarks>
    /// Chamado na rotação (a cada renovação) e, futuramente, no logout explícito.
    /// A operação é idempotente: revogar duas vezes apenas atualiza o instante,
    /// sem efeito colateral.
    /// </remarks>
    public void Revoke(DateTime revokedAtUtc)
    {
        IsRevoked = true;
        RevokedAtUtc = revokedAtUtc;
    }
}
