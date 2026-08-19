using BuildingBlocks.Application;
using FluentValidation;
using MediatR;
using Shouldly;

namespace BuildingBlocks.UnitTests;

/// <summary>
/// Testes do comportamento de pipeline que executa os validadores.
/// </summary>
/// <remarks>
/// Esta suíte guarda a correção de um bug real: os validadores estavam
/// registrados no contêiner de DI, mas nenhum <c>IPipelineBehavior</c> os
/// invocava — todas as regras eram código morto. Os testes abaixo travam o
/// comportamento correto para que a regressão não volte silenciosamente.
/// </remarks>
public sealed class ValidationBehaviorTests
{
    private sealed record SampleCommand(string Name) : IRequest<string>;

    private sealed class SampleCommandValidator : AbstractValidator<SampleCommand>
    {
        public SampleCommandValidator()
        {
            RuleFor(command => command.Name).NotEmpty().WithMessage("Nome é obrigatório.");
            RuleFor(command => command.Name).MinimumLength(3).WithMessage("Nome muito curto.");
        }
    }

    [Fact]
    public async Task Deve_executar_o_handler_quando_a_requisicao_e_valida()
    {
        var behavior = new ValidationBehavior<SampleCommand, string>([new SampleCommandValidator()]);
        var handlerFoiChamado = false;

        var resultado = await behavior.Handle(
            new SampleCommand("Bruno"),
            () =>
            {
                handlerFoiChamado = true;
                return Task.FromResult("ok");
            },
            CancellationToken.None);

        handlerFoiChamado.ShouldBeTrue();
        resultado.ShouldBe("ok");
    }

    [Fact]
    public async Task Deve_impedir_a_execucao_do_handler_quando_a_requisicao_e_invalida()
    {
        var behavior = new ValidationBehavior<SampleCommand, string>([new SampleCommandValidator()]);
        var handlerFoiChamado = false;

        // O ponto central: o handler NÃO pode rodar. Não basta a exceção ser
        // lançada depois — um handler que já gravou no banco antes de a validação
        // reprovar deixaria efeito colateral indevido.
        await Should.ThrowAsync<ValidationException>(async () => await behavior.Handle(
            new SampleCommand(string.Empty),
            () =>
            {
                handlerFoiChamado = true;
                return Task.FromResult("ok");
            },
            CancellationToken.None));

        handlerFoiChamado.ShouldBeFalse();
    }

    [Fact]
    public async Task Deve_agregar_todas_as_falhas_de_validacao_numa_unica_excecao()
    {
        var behavior = new ValidationBehavior<SampleCommand, string>([new SampleCommandValidator()]);

        // "" viola tanto NotEmpty quanto MinimumLength: o cliente deve receber as
        // duas mensagens de uma vez, e não descobrir a segunda só depois de
        // corrigir a primeira.
        var excecao = await Should.ThrowAsync<ValidationException>(async () => await behavior.Handle(
            new SampleCommand(string.Empty),
            () => Task.FromResult("ok"),
            CancellationToken.None));

        excecao.Errors.Count().ShouldBe(2);
        excecao.Errors.Select(error => error.ErrorMessage)
            .ShouldBe(["Nome é obrigatório.", "Nome muito curto."], ignoreOrder: true);
    }

    [Fact]
    public async Task Deve_seguir_direto_para_o_handler_quando_nao_ha_validador_registrado()
    {
        // A maioria das queries não tem validador. O behavior precisa ser
        // transparente nesse caso, sem custo nem efeito.
        var behavior = new ValidationBehavior<SampleCommand, string>([]);

        var resultado = await behavior.Handle(
            new SampleCommand(string.Empty),
            () => Task.FromResult("sem validacao"),
            CancellationToken.None);

        resultado.ShouldBe("sem validacao");
    }
}
