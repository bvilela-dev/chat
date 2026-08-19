using ChatService.Application.Messages;

namespace ChatService.UnitTests;

/// <summary>
/// Testes dos validadores do Chat Service.
/// </summary>
/// <remarks>
/// Estes validadores existiam antes, mas nunca eram executados — faltava o
/// <c>ValidationBehavior</c> no pipeline do MediatR. Testá-los diretamente
/// garante que as regras estão corretas; os testes do behavior, no projeto de
/// building blocks, garantem que elas de fato rodam.
/// </remarks>
public sealed class SendMessageCommandValidatorTests
{
    private readonly SendMessageCommandValidator _validator = new();

    private static SendMessageCommand ValidCommand(string content = "Olá!")
    {
        return new SendMessageCommand(Guid.NewGuid(), Guid.NewGuid(), "Bruno", content);
    }

    [Fact]
    public void Deve_aceitar_um_comando_valido()
    {
        _validator.Validate(ValidCommand()).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Deve_recusar_conteudo_vazio_ou_so_com_espacos(string content)
    {
        _validator.Validate(ValidCommand(content)).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Deve_recusar_conteudo_acima_do_limite_da_coluna()
    {
        // O limite espelha o HasMaxLength(4000) da tabela de mensagens.
        //
        // Sem esta regra, a mensagem seria transmitida em tempo real com sucesso
        // e falharia depois, silenciosamente, na persistência: o usuário veria a
        // mensagem enviada e ela sumiria ao recarregar a página.
        var conteudoLongoDemais = new string('a', 4001);

        _validator.Validate(ValidCommand(conteudoLongoDemais)).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Deve_aceitar_conteudo_exatamente_no_limite()
    {
        var conteudoNoLimite = new string('a', 4000);

        _validator.Validate(ValidCommand(conteudoNoLimite)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Deve_recusar_conversa_sem_identificador()
    {
        var comando = new SendMessageCommand(Guid.Empty, Guid.NewGuid(), "Bruno", "Olá!");

        _validator.Validate(comando).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Deve_recusar_remetente_sem_identificador()
    {
        var comando = new SendMessageCommand(Guid.NewGuid(), Guid.Empty, "Bruno", "Olá!");

        _validator.Validate(comando).IsValid.ShouldBeFalse();
    }
}
