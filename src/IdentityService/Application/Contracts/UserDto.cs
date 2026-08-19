namespace IdentityService.Application.Contracts;

/// <summary>
/// Representação pública de um usuário.
/// </summary>
/// <remarks>
/// <para>
/// Repare no que este DTO <b>não</b> tem: <c>PasswordHash</c> e a coleção de
/// refresh tokens. Essa omissão é o motivo de o DTO existir.
/// </para>
/// <para>
/// Serializar a entidade de domínio <c>User</c> diretamente na resposta HTTP
/// exporia o hash da senha em <c>GET /api/users</c>. Ainda que o BCrypt seja
/// resistente, publicar hashes permite ataque offline de dicionário — sem
/// nenhum limite de tentativas, porque o atacante já não precisa falar com a
/// nossa API. O DTO é a fronteira explícita entre "o que o domínio sabe" e "o
/// que o mundo externo pode ver".
/// </para>
/// </remarks>
/// <param name="Id">Identificador do usuário.</param>
/// <param name="Name">Nome de exibição.</param>
/// <param name="Email">E-mail normalizado.</param>
/// <param name="CreatedAtUtc">Instante do cadastro, em UTC.</param>
public sealed record UserDto(Guid Id, string Name, string Email, DateTime CreatedAtUtc);

/// <summary>
/// Resposta devolvida por cadastro, login e renovação de token.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que dois tokens.</b> É o compromisso clássico entre segurança e
/// experiência de uso:
/// </para>
/// <list type="bullet">
///   <item><description>
///   O <b>access token</b> é um JWT autocontido: qualquer serviço o valida
///   apenas com a chave, sem consultar banco. Isso é ótimo para desempenho e
///   péssimo para revogação — não há como cancelá-lo antes de expirar. Daí a
///   vida curta (15 minutos).
///   </description></item>
///   <item><description>
///   O <b>refresh token</b> é opaco e persistido. Cada uso vai ao banco, o que
///   o torna revogável de imediato, mas é usado raramente — só a cada 15
///   minutos — então o custo é irrelevante.
///   </description></item>
/// </list>
/// <para>
/// As datas de expiração são explícitas para que o cliente possa renovar
/// proativamente, em vez de descobrir o vencimento através de um 401 no meio de
/// uma ação do usuário.
/// </para>
/// </remarks>
public sealed record AuthResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    UserDto User);

/// <summary>
/// Par de tokens recém-emitido, antes de ser combinado com os dados do usuário.
/// </summary>
public sealed record TokenPair(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc);
