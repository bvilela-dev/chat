using Microsoft.IdentityModel.JsonWebTokens;
using System.Security.Claims;
using BuildingBlocks.Application;

namespace BuildingBlocks.AspNetCore;

/// <summary>
/// Leitura tipada da identidade contida no token JWT da requisição.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que isso existe.</b> Espalhados pelo código havia trechos como:
/// </para>
/// <code>
/// var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
/// return Guid.TryParse(value, out var userId) ? userId : throw new ...;
/// </code>
/// <para>
/// Repetidos em controllers e no Hub, com tratamento de erro diferente em cada
/// lugar. Além de duplicação, é uma superfície de bug sutil: o
/// <c>JwtSecurityTokenHandler</c> do .NET, por padrão, <b>remapeia</b> claims
/// curtas do padrão JWT para as URIs longas do WS-Federation — <c>sub</c> vira
/// <c>ClaimTypes.NameIdentifier</c>. Quem desativa esse remapeamento (prática
/// recomendada, para o token refletir literalmente o que foi emitido) quebra o
/// código que só procurava por <c>ClaimTypes.NameIdentifier</c>.
/// </para>
/// <para>
/// Centralizar aqui significa que a resposta a "quem é o usuário desta
/// requisição?" tem uma única implementação, testável e consistente.
/// </para>
/// </remarks>
public static class CurrentUser
{
    /// <summary>
    /// Extrai o identificador do usuário autenticado.
    /// </summary>
    /// <param name="principal">Identidade da requisição atual.</param>
    /// <returns>O <see cref="Guid"/> do usuário.</returns>
    /// <exception cref="UnauthorizedException">
    /// Lançada quando o token não traz um identificador utilizável. Na prática
    /// não deveria ocorrer em endpoint marcado com <c>[Authorize]</c>, mas tratar
    /// como falha explícita é infinitamente melhor do que seguir com
    /// <c>Guid.Empty</c> — que passaria despercebido e consultaria dados de
    /// "um usuário de id zerado".
    /// </exception>
    public static Guid GetRequiredUserId(this ClaimsPrincipal? principal)
    {
        return principal.TryGetUserId(out var userId)
            ? userId
            : throw new UnauthorizedException("O token não contém um identificador de usuário válido.");
    }

    /// <summary>
    /// Tenta extrair o identificador do usuário sem lançar exceção.
    /// </summary>
    public static bool TryGetUserId(this ClaimsPrincipal? principal, out Guid userId)
    {
        userId = Guid.Empty;

        if (principal is null)
        {
            return false;
        }

        // Verificamos as duas grafias justamente para ser imune ao remapeamento
        // de claims descrito acima, independentemente de como o host esteja
        // configurado.
        var rawUserId =
            principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(rawUserId, out userId);
    }

    /// <summary>
    /// Nome de exibição do usuário autenticado.
    /// </summary>
    /// <remarks>
    /// Cai para o identificador quando o nome não está presente: é preferível
    /// mostrar um GUID a quebrar o envio de uma mensagem por causa de um claim
    /// opcional ausente.
    /// </remarks>
    public static string GetDisplayName(this ClaimsPrincipal? principal)
    {
        var name =
            principal?.FindFirstValue(ClaimTypes.Name)
            ?? principal?.FindFirstValue(JwtRegisteredClaimNames.Name)
            ?? principal?.FindFirstValue("unique_name");

        return string.IsNullOrWhiteSpace(name)
            ? principal.GetRequiredUserId().ToString()
            : name;
    }

    /// <summary>
    /// Garante que o usuário autenticado é o mesmo do recurso solicitado.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Utilitário para o padrão de defesa contra <b>IDOR</b> (Insecure Direct
    /// Object Reference): quando um endpoint recebe um identificador de usuário
    /// pela rota, ele precisa conferir que aquele identificador pertence a quem
    /// está chamando. Sem essa checagem, trocar o GUID na URL dá acesso aos dados
    /// de outra pessoa.
    /// </para>
    /// <para>
    /// Vale notar que a defesa <i>mais forte</i> é não aceitar o identificador na
    /// rota — foi o caminho adotado nos endpoints de conversas e de presença,
    /// que passaram a derivar o usuário exclusivamente do token. Um endpoint que
    /// não recebe o id não tem como confundi-lo. Este método fica para os casos
    /// em que o parâmetro precisa existir por compatibilidade.
    /// </para>
    /// </remarks>
    /// <exception cref="ForbiddenException">Quando os identificadores divergem.</exception>
    public static void EnsureIsSelf(this ClaimsPrincipal? principal, Guid requestedUserId)
    {
        if (principal.GetRequiredUserId() != requestedUserId)
        {
            // Mensagem deliberadamente vaga: confirmar "esse usuário existe, mas
            // não é você" já entrega informação sobre a base de usuários.
            throw new ForbiddenException("Você não tem permissão para acessar este recurso.");
        }
    }
}
