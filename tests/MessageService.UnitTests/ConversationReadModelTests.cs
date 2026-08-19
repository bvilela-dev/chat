using MessageService.Domain;
using MessageService.UnitTests.TestDoubles;

namespace MessageService.UnitTests;

/// <summary>
/// Testes da projeção de resumo de conversa.
/// </summary>
public sealed class ConversationReadModelTests
{
    [Fact]
    public void Deve_registrar_a_primeira_mensagem()
    {
        var readModel = ConversationReadModel.Create(Guid.NewGuid());

        readModel.Update("primeira mensagem", FixedClock.DefaultInstant);

        readModel.LastMessage.ShouldBe("primeira mensagem");
        readModel.LastMessageAtUtc.ShouldBe(FixedClock.DefaultInstant);
    }

    [Fact]
    public void Deve_atualizar_com_uma_mensagem_mais_recente()
    {
        var readModel = ConversationReadModel.Create(Guid.NewGuid());
        readModel.Update("antiga", FixedClock.DefaultInstant);

        readModel.Update("nova", FixedClock.DefaultInstant.AddMinutes(1));

        readModel.LastMessage.ShouldBe("nova");
    }

    [Fact]
    public void Deve_ignorar_uma_mensagem_mais_antiga_que_a_atual()
    {
        // PROTEÇÃO CONTRA EVENTO FORA DE ORDEM.
        //
        // A entrega do RabbitMQ não garante ordenação global, e uma retentativa
        // pode reentregar um evento antigo depois de um novo. Sem esta guarda,
        // uma mensagem antiga reprocessada sobrescreveria a prévia atual — e a
        // lista de conversas mostraria conteúdo desatualizado. É um bug
        // intermitente, que só aparece sob carga e é muito difícil de reproduzir.
        var readModel = ConversationReadModel.Create(Guid.NewGuid());
        readModel.Update("mensagem mais recente", FixedClock.DefaultInstant);

        readModel.Update("mensagem antiga reprocessada", FixedClock.DefaultInstant.AddMinutes(-10));

        readModel.LastMessage.ShouldBe("mensagem mais recente");
        readModel.LastMessageAtUtc.ShouldBe(FixedClock.DefaultInstant);
    }

    [Fact]
    public void Deve_ignorar_uma_mensagem_com_o_mesmo_instante()
    {
        // Reprocessamento exato do MESMO evento: o resultado precisa ser
        // idêntico, sem efeito colateral. É a definição de idempotência.
        var readModel = ConversationReadModel.Create(Guid.NewGuid());
        readModel.Update("original", FixedClock.DefaultInstant);

        readModel.Update("duplicata", FixedClock.DefaultInstant);

        readModel.LastMessage.ShouldBe("original");
    }
}
