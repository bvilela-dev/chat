using System.Text.Json;
using BuildingBlocks.Application;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.AspNetCore;

/// <summary>
/// Traduz exceções não tratadas em respostas <c>application/problem+json</c>
/// (RFC 7807), padronizando o formato de erro de toda a plataforma.
/// </summary>
/// <remarks>
/// <para>
/// Substitui quatro middlewares quase idênticos — um por serviço — que divergiam
/// em detalhes relevantes: um deles mapeava <c>ConflictException</c> para 409,
/// os outros deixavam a mesma exceção virar 500. O cliente recebia respostas
/// diferentes para o mesmo tipo de erro dependendo de qual serviço atendeu.
/// </para>
/// <para>
/// <b>Por que RFC 7807 e não um JSON qualquer.</b> É o formato que o próprio
/// ASP.NET Core já emite para erros de model binding, e que bibliotecas de
/// cliente sabem interpretar. Padronizar significa que o frontend tem <b>um</b>
/// caminho de tratamento de erro, não um por endpoint.
/// </para>
/// </remarks>
public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Executa o próximo middleware capturando falhas.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ValidationException exception)
        {
            // Vem do ValidationBehavior do MediatR. Carrega a lista de campos
            // inválidos, que é devolvida ao cliente para exibição no formulário.
            await WriteProblemAsync(
                context,
                statusCode: StatusCodes.Status400BadRequest,
                errorCode: "validation_failed",
                title: "A requisição contém campos inválidos.",
                errors: exception.Errors
                    .GroupBy(failure => failure.PropertyName)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(failure => failure.ErrorMessage).ToArray()));
        }
        catch (ApplicationRuleException exception)
        {
            // Falha de negócio prevista: a mensagem foi escrita para ser lida
            // pelo usuário final, então pode ser repassada com segurança.
            // Nível Warning, não Error — não indica defeito no sistema e não deve
            // disparar alerta de plantão.
            logger.LogWarning(
                "Regra de negócio violada em {Path}: {ErrorCode} - {Message}",
                context.Request.Path,
                exception.ErrorCode,
                exception.Message);

            await WriteProblemAsync(
                context,
                statusCode: exception.StatusCode,
                errorCode: exception.ErrorCode,
                title: exception.Message,
                errors: null);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // O cliente desistiu da requisição (fechou a aba, perdeu a rede).
            // Não é erro: registrar como falha polui as métricas e gera ruído em
            // alertas. O código 499 é a convenção do nginx para "client closed request".
            logger.LogDebug("Requisição cancelada pelo cliente em {Path}.", context.Request.Path);

            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = 499;
            }
        }
        catch (Exception exception)
        {
            // Defeito inesperado. O stack trace vai para o log (onde a equipe tem
            // acesso) e o cliente recebe apenas uma mensagem genérica.
            //
            // Devolver `exception.Message` aqui seria um vazamento clássico de
            // informação: mensagens de erro do Npgsql expõem nome de host, banco
            // e usuário; as do EF Core expõem o schema das tabelas.
            logger.LogError(
                exception,
                "Exceção não tratada ao processar {Method} {Path}.",
                context.Request.Method,
                context.Request.Path);

            await WriteProblemAsync(
                context,
                statusCode: StatusCodes.Status500InternalServerError,
                errorCode: "internal_error",
                title: "Ocorreu um erro inesperado no servidor.",
                errors: null);
        }
    }

    private static async Task WriteProblemAsync(
        HttpContext context,
        int statusCode,
        string errorCode,
        string title,
        IReadOnlyDictionary<string, string[]>? errors)
    {
        // Se a resposta já começou a ser enviada, os cabeçalhos foram para a rede
        // e alterá-los agora lançaria outra exceção — mascarando a original.
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        var problem = new
        {
            type = $"https://httpstatuses.io/{statusCode}",
            title,
            status = statusCode,
            errorCode,
            // Correlaciona a resposta com o trace do OpenTelemetry: o usuário
            // reporta este identificador e a equipe localiza a requisição exata
            // no Grafana/Jaeger, com todos os spans dos serviços envolvidos.
            traceId = System.Diagnostics.Activity.Current?.Id ?? context.TraceIdentifier,
            errors
        };

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(problem, SerializerOptions),
            context.RequestAborted);
    }
}

/// <summary>Atalhos de registro do middleware de exceções.</summary>
public static class ExceptionHandlingMiddlewareExtensions
{
    /// <summary>
    /// Adiciona o tratamento padronizado de exceções ao pipeline.
    /// </summary>
    /// <remarks>
    /// Deve ser o <b>primeiro</b> middleware registrado. O pipeline do ASP.NET
    /// Core é uma pilha: só é possível capturar exceções de componentes
    /// registrados <i>depois</i>. Colocá-lo após <c>UseAuthentication</c>, por
    /// exemplo, deixaria falhas do próprio processo de autenticação escaparem
    /// sem tratamento.
    /// </remarks>
    public static IApplicationBuilder UseChatExceptionHandling(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ExceptionHandlingMiddleware>();
    }
}
