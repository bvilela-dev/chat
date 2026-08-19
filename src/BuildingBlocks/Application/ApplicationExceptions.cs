namespace BuildingBlocks.Application;

/// <summary>
/// Classe base das exceções que representam uma <b>falha esperada de negócio</b>,
/// e não um defeito do sistema.
/// </summary>
/// <remarks>
/// <para>
/// A distinção é o ponto central deste arquivo. Existem dois tipos de erro:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///     <b>Falha esperada</b> — "e-mail já cadastrado", "senha inválida", "você
///     não participa dessa conversa". Faz parte do contrato da API. O cliente
///     precisa saber exatamente o que aconteceu para corrigir. Vira 4xx e é
///     logada em nível <c>Information</c>/<c>Warning</c>.
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>Defeito</b> — <c>NullReferenceException</c>, timeout de banco, bug de
///     serialização. O cliente não pode fazer nada a respeito. Vira 500, a
///     mensagem interna <b>nunca</b> é exposta (evita vazar connection string,
///     caminho de arquivo ou stack trace) e é logada em nível <c>Error</c>.
///     </description>
///   </item>
/// </list>
/// <para>
/// Herdar de <see cref="ApplicationRuleException"/> permite que um único
/// middleware traduza a exceção para o status HTTP correto, sem que a camada de
/// aplicação precise conhecer HTTP. É por isso que a exceção carrega o
/// <see cref="StatusCode"/>: a <i>intenção</i> ("isso é um conflito") nasce no
/// domínio; a <i>representação</i> (o número 409) é resolvida na borda.
/// </para>
/// </remarks>
public abstract class ApplicationRuleException(string message, int statusCode) : Exception(message)
{
    /// <summary>Status HTTP que melhor representa esta falha de negócio.</summary>
    public int StatusCode { get; } = statusCode;

    /// <summary>
    /// Identificador curto e estável do tipo de erro, pensado para ser consumido
    /// por código (o cliente pode ramificar em <c>"conflict"</c>) em vez de
    /// depender do texto da mensagem, que pode mudar ou ser traduzido.
    /// </summary>
    public abstract string ErrorCode { get; }
}

/// <summary>
/// O estado atual do recurso impede a operação (HTTP 409).
/// Exemplo: cadastrar um usuário com um e-mail que já existe.
/// </summary>
public sealed class ConflictException(string message) : ApplicationRuleException(message, 409)
{
    /// <inheritdoc />
    public override string ErrorCode => "conflict";
}

/// <summary>
/// O chamador não se identificou, ou as credenciais apresentadas não conferem (HTTP 401).
/// </summary>
/// <remarks>
/// Usada no login e no refresh token. Repare que o handler de login lança a
/// <b>mesma</b> mensagem tanto para "e-mail não existe" quanto para "senha
/// errada". Isso é deliberado: mensagens distintas transformam o endpoint de
/// login num oráculo de enumeração de usuários, permitindo que um atacante
/// descubra quais e-mails estão cadastrados.
/// </remarks>
public sealed class UnauthorizedException(string message) : ApplicationRuleException(message, 401)
{
    /// <inheritdoc />
    public override string ErrorCode => "unauthorized";
}

/// <summary>
/// O chamador está autenticado, mas não tem permissão sobre este recurso (HTTP 403).
/// </summary>
/// <remarks>
/// A diferença entre 401 e 403 costuma ser cobrada em entrevista:
/// <b>401 = "não sei quem você é"</b> (autenticação);
/// <b>403 = "sei quem você é, e você não pode"</b> (autorização).
/// Aqui é o retorno de tentar ler uma conversa da qual o usuário não participa.
/// </remarks>
public sealed class ForbiddenException(string message) : ApplicationRuleException(message, 403)
{
    /// <inheritdoc />
    public override string ErrorCode => "forbidden";
}

/// <summary>
/// O recurso solicitado não existe (HTTP 404).
/// </summary>
public sealed class NotFoundException(string message) : ApplicationRuleException(message, 404)
{
    /// <inheritdoc />
    public override string ErrorCode => "not_found";
}
