using FluentValidation;
using MediatR;

namespace BuildingBlocks.Application;

/// <summary>
/// Comportamento de pipeline do MediatR que executa os validadores do
/// FluentValidation antes de qualquer handler receber o comando ou a query.
/// </summary>
/// <typeparam name="TRequest">Comando ou query sendo despachado.</typeparam>
/// <typeparam name="TResponse">Resposta produzida pelo handler.</typeparam>
/// <remarks>
/// <para>
/// <b>Contexto — o bug que esta classe corrige.</b> Os serviços já chamavam
/// <c>AddValidatorsFromAssembly(...)</c> no <c>Program.cs</c>, o que registra os
/// validadores no contêiner de injeção de dependência. Só que registrar um
/// validador não faz ninguém executá-lo: sem um <see cref="IPipelineBehavior{TRequest, TResponse}"/>
/// para invocá-los, todas as regras (<c>MinimumLength(8)</c> na senha,
/// <c>MaximumLength(4000)</c> no conteúdo da mensagem, <c>EmailAddress()</c>...)
/// eram <b>código morto</b>. O sistema aceitava senha de 1 caractere e mensagem
/// de tamanho arbitrário.
/// </para>
/// <para>
/// <b>Como funciona o pipeline.</b> O MediatR monta uma cadeia parecida com o
/// pipeline de middlewares do ASP.NET Core:
/// </para>
/// <code>
/// Send(comando)
///   └─> ValidationBehavior      ← valida; se falhar, corta aqui e o handler nunca roda
///         └─> (outros behaviors: log, transação, métricas...)
///               └─> Handler     ← só recebe entrada já garantidamente válida
/// </code>
/// <para>
/// <b>Por que validar aqui e não dentro do handler?</b> Três motivos:
/// </para>
/// <list type="number">
///   <item><description>
///   <i>Não é possível esquecer.</i> Um handler novo passa a ser validado
///   automaticamente pelo simples fato de existir um validador para o comando —
///   não depende de o desenvolvedor lembrar de chamar <c>Validate()</c>.
///   </description></item>
///   <item><description>
///   <i>O handler fica limpo.</i> Ele expressa a regra de negócio, não a
///   checagem de formato. Menos ruído, testes mais focados.
///   </description></item>
///   <item><description>
///   <i>Vale para qualquer porta de entrada.</i> Um comando disparado por um
///   controller REST, por um método do Hub SignalR ou por um consumidor do
///   RabbitMQ passa exatamente pelas mesmas validações.
///   </description></item>
/// </list>
/// <para>
/// <b>Decisão de projeto: agregar todos os erros.</b> Rodamos todos os
/// validadores e juntamos as falhas numa única exceção, em vez de parar no
/// primeiro problema. Um formulário que reporta "e-mail inválido" e só depois de
/// corrigido revela "senha muito curta" é frustrante; o cliente recebe a lista
/// completa de uma vez.
/// </para>
/// </remarks>
public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>
    /// Valida a requisição e, se estiver íntegra, entrega o controle ao próximo
    /// elo do pipeline.
    /// </summary>
    /// <exception cref="ValidationException">
    /// Lançada quando ao menos uma regra falha. O middleware HTTP a converte em
    /// uma resposta 400 com o detalhamento por campo.
    /// </exception>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Caminho rápido: a maioria das queries não tem validador registrado.
        // Materializar a lista evita enumerar o IEnumerable do contêiner duas vezes.
        var applicableValidators = validators as IReadOnlyList<IValidator<TRequest>> ?? [.. validators];
        if (applicableValidators.Count == 0)
        {
            return await next();
        }

        var validationContext = new ValidationContext<TRequest>(request);

        // Os validadores são independentes entre si, então rodam em paralelo.
        // Na prática são operações em memória e baratas — o ganho aqui é mais de
        // uniformidade (suportar validadores assíncronos, que consultam banco)
        // do que de desempenho.
        var validationResults = await Task.WhenAll(
            applicableValidators.Select(validator => validator.ValidateAsync(validationContext, cancellationToken)));

        var failures = validationResults
            .Where(result => !result.IsValid)
            .SelectMany(result => result.Errors)
            .ToArray();

        if (failures.Length > 0)
        {
            // Interrompe o pipeline: `next` nunca é chamado e o handler não executa.
            throw new ValidationException(failures);
        }

        return await next();
    }
}
