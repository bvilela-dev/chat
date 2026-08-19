using MessageService.Application.Abstractions;
using MessageService.Application.Contracts;
using MessageService.Application.Conversations;
using MessageService.UnitTests.TestDoubles;

namespace MessageService.UnitTests;

/// <summary>
/// Testes da listagem de conversas do usuário.
/// </summary>
public sealed class GetUserConversationsQueryHandlerTests
{
    private readonly IMessageRepository _repository = Substitute.For<IMessageRepository>();

    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ContactId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private GetUserConversationsQueryHandler CreateHandler() => new(_repository);

    private void ArrangeConversations(params ConversationSummary[] conversations)
    {
        _repository.GetUserConversationsAsync(UserId, Arg.Any<CancellationToken>()).Returns(conversations);
    }

    [Fact]
    public async Task Deve_ordenar_as_conversas_pela_atividade_mais_recente()
    {
        var antiga = new ConversationSummary(
            Guid.NewGuid(), "antiga", FixedClock.DefaultInstant.AddHours(-2), false, Guid.NewGuid());
        var recente = new ConversationSummary(
            Guid.NewGuid(), "recente", FixedClock.DefaultInstant, false, Guid.NewGuid());

        ArrangeConversations(antiga, recente);

        var resultado = await CreateHandler().Handle(new GetUserConversationsQuery(UserId), CancellationToken.None);

        resultado.First().LastMessage.ShouldBe("recente");
    }

    [Fact]
    public async Task Deve_colocar_conversas_sem_mensagem_no_final()
    {
        var semMensagem = new ConversationSummary(Guid.NewGuid(), string.Empty, null, false, Guid.NewGuid());
        var comMensagem = new ConversationSummary(
            Guid.NewGuid(), "oi", FixedClock.DefaultInstant, false, Guid.NewGuid());

        ArrangeConversations(semMensagem, comMensagem);

        var resultado = await CreateHandler().Handle(new GetUserConversationsQuery(UserId), CancellationToken.None);

        resultado.Last().LastMessageAtUtc.ShouldBeNull();
    }

    [Fact]
    public async Task Deve_exibir_apenas_a_conversa_mais_recente_por_contato()
    {
        // DEDUPLICAÇÃO POR CONTRAPARTE.
        //
        // Sob concorrência, dois usuários que abrem a conversa um com o outro ao
        // mesmo tempo podem criar duas conversas diretas para o mesmo par: ambos
        // consultam "existe?", ambos recebem "não", ambos criam.
        //
        // A interface não pode mostrar a mesma pessoa duas vezes. (Este é um
        // paliativo de apresentação; a correção definitiva é uma restrição de
        // unicidade no banco, registrada como próximo passo no README.)
        var duplicataAntiga = new ConversationSummary(
            Guid.NewGuid(), "antiga", FixedClock.DefaultInstant.AddHours(-1), false, ContactId);
        var duplicataRecente = new ConversationSummary(
            Guid.NewGuid(), "recente", FixedClock.DefaultInstant, false, ContactId);

        ArrangeConversations(duplicataAntiga, duplicataRecente);

        var resultado = await CreateHandler().Handle(new GetUserConversationsQuery(UserId), CancellationToken.None);

        resultado.Count.ShouldBe(1);
        resultado.Single().LastMessage.ShouldBe("recente");
    }

    [Fact]
    public async Task Nao_deve_deduplicar_conversas_em_grupo()
    {
        // Grupos são identificados pelo próprio id, e não pela contraparte: dois
        // grupos distintos precisam aparecer como duas linhas.
        var primeiroGrupo = new ConversationSummary(
            Guid.NewGuid(), "grupo A", FixedClock.DefaultInstant, true, null);
        var segundoGrupo = new ConversationSummary(
            Guid.NewGuid(), "grupo B", FixedClock.DefaultInstant.AddMinutes(-1), true, null);

        ArrangeConversations(primeiroGrupo, segundoGrupo);

        var resultado = await CreateHandler().Handle(new GetUserConversationsQuery(UserId), CancellationToken.None);

        resultado.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Deve_descartar_conversas_diretas_sem_contraparte()
    {
        // Registro incompleto (uma projeção que ficou pela metade): não teria
        // como ser exibido na interface, então é filtrado.
        var incompleta = new ConversationSummary(
            Guid.NewGuid(), "orfa", FixedClock.DefaultInstant, false, null);

        ArrangeConversations(incompleta);

        var resultado = await CreateHandler().Handle(new GetUserConversationsQuery(UserId), CancellationToken.None);

        resultado.ShouldBeEmpty();
    }

    [Fact]
    public async Task Deve_devolver_lista_vazia_quando_o_usuario_nao_tem_conversas()
    {
        ArrangeConversations();

        var resultado = await CreateHandler().Handle(new GetUserConversationsQuery(UserId), CancellationToken.None);

        resultado.ShouldBeEmpty();
    }
}
