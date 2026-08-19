using IdentityService.Application.Abstractions;

namespace IdentityService.Infrastructure.Security;

/// <summary>
/// Implementação de <see cref="IPasswordHasher"/> com BCrypt.
/// </summary>
public sealed class BcryptPasswordHasher : IPasswordHasher
{
    /// <summary>
    /// Fator de custo (work factor) do BCrypt.
    /// </summary>
    /// <remarks>
    /// <para>
    /// O custo é <b>exponencial</b>: cada incremento dobra o tempo de cálculo.
    /// O padrão da biblioteca é 11; usamos 12, que em hardware de servidor atual
    /// fica em torno de 200–300 ms.
    /// </para>
    /// <para>
    /// A escolha é um equilíbrio explícito. Alto demais e o login fica lento,
    /// além de virar um vetor de negação de serviço contra a própria CPU do
    /// serviço. Baixo demais e o ataque offline a um dump de banco fica barato
    /// para quem tem GPUs. A recomendação da OWASP é calibrar o fator para algo
    /// entre 250 ms e 1 s no hardware de produção, e revisar periodicamente —
    /// esse valor deve subir com o tempo, e não ficar congelado.
    /// </para>
    /// </remarks>
    private const int WorkFactor = 12;

    /// <summary>
    /// Hash fixo, gerado uma única vez, usado para equalizar o tempo de resposta
    /// do login quando o e-mail não existe.
    /// </summary>
    /// <remarks>
    /// Calculado na inicialização estática da classe, e não a cada chamada: gerar
    /// um hash novo por requisição custaria o dobro do trabalho no caminho de
    /// falha, o que seria um amplificador de DoS em vez de uma proteção.
    /// </remarks>
    private static readonly string DummyHash =
        BCrypt.Net.BCrypt.HashPassword("timing-attack-mitigation-placeholder", WorkFactor);

    /// <inheritdoc />
    public string Hash(string value)
    {
        // O salt aleatório é gerado internamente pelo BCrypt e embutido no hash
        // resultante — por isso não existe uma coluna "salt" na tabela.
        return BCrypt.Net.BCrypt.HashPassword(value, WorkFactor);
    }

    /// <inheritdoc />
    public bool Verify(string value, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(value, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            // Hash corrompido ou em formato desconhecido (por exemplo, um
            // registro migrado de um sistema legado que usava outro algoritmo).
            // Tratar como "senha não confere" é o comportamento seguro: deixar a
            // exceção subir viraria um 500 e revelaria, pela diferença de
            // resposta, que aquele usuário tem um hash inválido.
            return false;
        }
    }

    /// <inheritdoc />
    public bool VerifyAgainstDummyHash(string value)
    {
        // O resultado é irrelevante e sempre descartado. O que importa é ter
        // gasto o mesmo tempo de CPU do caminho em que o usuário existe.
        _ = BCrypt.Net.BCrypt.Verify(value, DummyHash);
        return false;
    }
}
