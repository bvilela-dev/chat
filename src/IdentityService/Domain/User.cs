namespace IdentityService.Domain;

/// <summary>
/// Raiz de agregado que representa uma conta de usuário e seus refresh tokens.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que um modelo "rico" e não um saco de propriedades públicas.</b> Repare
/// que todos os <c>set</c> são privados e que não existe construtor público. A
/// única forma de criar um usuário é <see cref="Register"/>, e a única forma de
/// emitir um token é <see cref="IssueRefreshToken"/>.
/// </para>
/// <para>
/// A consequência é que <b>não existe usuário em estado inválido</b> no sistema.
/// Com propriedades abertas, seria possível escrever
/// <c>new User { Email = "" }</c> em qualquer canto da base — e alguém
/// escreveria, provavelmente num script de importação sem revisão. Fechando o
/// acesso, as invariantes ficam garantidas por construção e não por disciplina.
/// </para>
/// <para>
/// <b>Por que a entidade é uma raiz de agregado.</b> <see cref="RefreshToken"/>
/// não tem existência independente: só faz sentido dentro de um usuário, e todo
/// acesso a ele passa por aqui. Isso concentra a regra "um token só é válido se
/// não estiver revogado e não tiver expirado" num lugar só, em vez de espalhá-la
/// pelos handlers.
/// </para>
/// </remarks>
public sealed class User
{
    private readonly List<RefreshToken> _refreshTokens = [];

    /// <summary>
    /// Construtor sem parâmetros exigido pelo Entity Framework Core para
    /// materializar a entidade vinda do banco.
    /// </summary>
    /// <remarks>
    /// Privado de propósito: o EF o acessa por reflexão, mas o código da
    /// aplicação continua obrigado a passar por <see cref="Register"/>.
    /// </remarks>
    private User()
    {
    }

    private User(Guid id, string name, string email, string passwordHash, DateTime createdAtUtc)
    {
        Id = id;
        Name = name;
        Email = email;
        PasswordHash = passwordHash;
        CreatedAtUtc = createdAtUtc;
    }

    /// <summary>Identificador único do usuário.</summary>
    public Guid Id { get; private set; }

    /// <summary>Nome de exibição.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>E-mail normalizado (minúsculas, sem espaços nas pontas).</summary>
    public string Email { get; private set; } = string.Empty;

    /// <summary>
    /// Hash BCrypt da senha.
    /// </summary>
    /// <remarks>
    /// A senha em texto claro <b>nunca</b> entra nesta classe: quem faz o hash é
    /// a camada de aplicação, antes de chamar <see cref="Register"/>. Assim o
    /// domínio não depende de nenhuma biblioteca de criptografia e não há risco
    /// de a senha original acabar num log de depuração da entidade.
    /// </remarks>
    public string PasswordHash { get; private set; } = string.Empty;

    /// <summary>Instante do cadastro, em UTC.</summary>
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>
    /// Refresh tokens já emitidos para este usuário.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Devolve <c>AsReadOnly()</c>, e não o campo <c>List&lt;T&gt;</c> direto.
    /// </para>
    /// <para>
    /// A diferença é sutil e importante. Declarar o retorno como
    /// <see cref="IReadOnlyCollection{T}"/> mas entregar a lista real protege
    /// apenas contra o descuido: quem quiser ainda pode escrever
    /// <c>((List&lt;RefreshToken&gt;)user.RefreshTokens).Add(...)</c> e driblar
    /// toda a lógica de emissão. Com <c>AsReadOnly()</c>, o encapsulamento passa
    /// a ser real — a conversão simplesmente falha.
    /// </para>
    /// <para>
    /// (Esta correção veio de um teste que verificava exatamente essa
    /// propriedade, e que reprovou na primeira execução.)
    /// </para>
    /// </remarks>
    public IReadOnlyCollection<RefreshToken> RefreshTokens => _refreshTokens.AsReadOnly();

    /// <summary>
    /// Normaliza um e-mail para comparação e armazenamento.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Vive no domínio, e não numa classe utilitária qualquer, porque é a
    /// definição canônica de "quando dois e-mails são o mesmo e-mail" — uma
    /// regra de negócio. Ter uma única implementação garante que o cadastro, o
    /// login e o índice único do banco concordem entre si.
    /// </para>
    /// <para>
    /// <c>ToLowerInvariant</c> e não <c>ToLower</c>: a cultura turca mapeia o
    /// 'I' maiúsculo para 'ı' (i sem pingo), então <c>ToLower()</c> num servidor
    /// com locale turco produziria uma chave diferente da de um servidor em
    /// pt-BR. É o clássico "problema do I turco", e a fonte de bugs de
    /// autenticação que só aparecem em uma região.
    /// </para>
    /// </remarks>
    public static string NormalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Cria um novo usuário.
    /// </summary>
    /// <param name="name">Nome de exibição (espaços das pontas são removidos).</param>
    /// <param name="email">E-mail; é normalizado internamente.</param>
    /// <param name="passwordHash">Hash já derivado pela camada de aplicação.</param>
    /// <param name="createdAtUtc">Instante do cadastro, fornecido pelo relógio injetado.</param>
    public static User Register(string name, string email, string passwordHash, DateTime createdAtUtc)
    {
        return new User(
            Guid.NewGuid(),
            name.Trim(),
            NormalizeEmail(email),
            passwordHash,
            createdAtUtc);
    }

    /// <summary>
    /// Emite um refresh token e o vincula a este usuário.
    /// </summary>
    public RefreshToken IssueRefreshToken(string token, DateTime expiresAtUtc, DateTime createdAtUtc)
    {
        var refreshToken = RefreshToken.Create(Id, token, expiresAtUtc, createdAtUtc);
        _refreshTokens.Add(refreshToken);
        return refreshToken;
    }

    /// <summary>
    /// Localiza um refresh token que ainda esteja utilizável no instante informado.
    /// </summary>
    /// <returns>O token ativo, ou <c>null</c> se não existir, estiver revogado ou expirado.</returns>
    public RefreshToken? GetActiveRefreshToken(string token, DateTime utcNow)
    {
        // Comparação ordinal explícita: token é um identificador opaco, e
        // comparar com regras de cultura seria semanticamente errado (além de
        // mais lento).
        return _refreshTokens.SingleOrDefault(candidate =>
            string.Equals(candidate.Token, token, StringComparison.Ordinal) &&
            candidate.IsActive(utcNow));
    }
}
