namespace IdentityService.Application.Abstractions;

/// <summary>
/// Derivação e verificação de hashes de senha.
/// </summary>
/// <remarks>
/// <para>
/// A abstração isola o algoritmo escolhido (aqui, BCrypt) da regra de negócio.
/// Isso importa porque algoritmos de senha <b>envelhecem</b>: MD5 e SHA-1 já
/// foram considerados adequados. Quando for hora de migrar para Argon2id, a
/// mudança fica contida na infraestrutura.
/// </para>
/// <para>
/// <b>Por que BCrypt e não SHA-256.</b> Funções de hash comuns são projetadas
/// para serem <i>rápidas</i> — o que, para senhas, é exatamente o defeito: uma
/// GPU testa bilhões de candidatas por segundo. BCrypt é deliberadamente lento e
/// tem um fator de custo ajustável, que pode ser elevado conforme o hardware
/// evolui. Ele também embute um <i>salt</i> aleatório por senha, o que impede
/// tabelas pré-computadas (rainbow tables) e faz com que dois usuários com a
/// mesma senha tenham hashes diferentes.
/// </para>
/// </remarks>
public interface IPasswordHasher
{
    /// <summary>Gera o hash da senha em texto claro.</summary>
    string Hash(string value);

    /// <summary>Confere uma senha em texto claro contra o hash armazenado.</summary>
    bool Verify(string value, string hash);

    /// <summary>
    /// Executa uma verificação descartável, contra um hash fixo, e sempre
    /// devolve <c>false</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Existe unicamente para <b>equalizar o tempo de resposta</b> do login.
    /// </para>
    /// <para>
    /// Sem ela, o fluxo natural seria: e-mail não encontrado no banco → retorna
    /// erro imediatamente (~1 ms). E-mail encontrado com senha errada → roda o
    /// BCrypt e só então retorna erro (~100 ms). Um atacante que cronometre as
    /// respostas distingue as duas situações com facilidade e enumera quais
    /// e-mails existem na base — anulando o cuidado de usar a mesma mensagem de
    /// erro para os dois casos.
    /// </para>
    /// <para>
    /// Gastando o mesmo esforço computacional no caminho do "usuário
    /// inexistente", os dois cenários passam a levar o mesmo tempo. É a mesma
    /// técnica usada pelo <c>django.contrib.auth</c> e por outras bibliotecas de
    /// autenticação maduras.
    /// </para>
    /// </remarks>
    bool VerifyAgainstDummyHash(string value);
}
