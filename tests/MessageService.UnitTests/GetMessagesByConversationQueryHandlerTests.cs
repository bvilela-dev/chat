using BuildingBlocks.Application;
using MessageService.Application.Abstractions;
using MessageService.Application.Messages;
using MessageService.Domain;
using MessageService.UnitTests.TestDoubles;

namespace MessageService.UnitTests;

/// <summary>
/// Testes da leitura de histórico, com foco na correção do IDOR.
/// </summary>
/// <remarks>
/// A versão anterior desta query não recebia o solicitante e não verificava
/// nada: qualquer usuário autenticado lia o histórico de qualquer conversa
/// trocando o GUID na URL. É a falha nº 1 do OWASP Top 10 (Broken Access
/// Control).
/// </remarks>
public sealed class GetMessagesByConversationQueryHandlerTests
{
    private readonly IMessageRepository _repository = Substitute.For<IMessageRepository>();

    private static readonly Guid ConversationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ParticipantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid IntruderId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private GetMessagesByConversationQueryHandler CreateHandler() => new(_repository);

    [Fact]
    public async Task Deve_bloquear_a_leitura_por_quem_nao_participa_da_conversa()
    {
        // TESTE DE REGRESSÃO DA FALHA DE CONTROLE DE ACESSO.
        _repository
            .IsParticipantAsync(ConversationId, IntruderId, Arg.Any<CancellationToken>())
            .Returns(false);

        await Should.ThrowAsync<ForbiddenException>(() => CreateHandler().Handle(
            new GetMessagesByConversationQuery(ConversationId, IntruderId),
            CancellationToken.None));
    }

    [Fact]
    public async Task Nao_deve_nem_consultar_as_mensagens_quando_o_acesso_e_negado()
    {
        // A verificação precisa acontecer ANTES de qualquer leitura. Buscar
        // primeiro e filtrar depois deixaria os dados passarem pela memória do
        // processo, com risco de vazarem por log de diagnóstico ou por uma
        // mensagem de erro detalhada.
        _repository
            .IsParticipantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(false);

        await Should.ThrowAsync<ForbiddenException>(() => CreateHandler().Handle(
            new GetMessagesByConversationQuery(ConversationId, IntruderId),
            CancellationToken.None));

        await _repository.DidNotReceive().GetMessagesByConversationAsync(
            Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deve_devolver_o_historico_para_quem_participa()
    {
        _repository
            .IsParticipantAsync(ConversationId, ParticipantId, Arg.Any<CancellationToken>())
            .Returns(true);

        _repository
            .GetMessagesByConversationAsync(ConversationId, 1, 50, Arg.Any<CancellationToken>())
            .Returns([
                MessageReadModel.Create(
                    Guid.NewGuid(), ConversationId, ParticipantId, "Bruno", "Olá!", FixedClock.DefaultInstant)
            ]);

        var mensagens = await CreateHandler().Handle(
            new GetMessagesByConversationQuery(ConversationId, ParticipantId),
            CancellationToken.None);

        mensagens.Count.ShouldBe(1);
        mensagens.Single().Content.ShouldBe("Olá!");
    }

    [Fact]
    public async Task Deve_repassar_os_parametros_de_paginacao_ao_repositorio()
    {
        _repository
            .IsParticipantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _repository
            .GetMessagesByConversationAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);

        await CreateHandler().Handle(
            new GetMessagesByConversationQuery(ConversationId, ParticipantId, Page: 3, PageSize: 25),
            CancellationToken.None);

        await _repository.Received(1).GetMessagesByConversationAsync(
            ConversationId, 3, 25, Arg.Any<CancellationToken>());
    }
}

/// <summary>Testes das regras de paginação do histórico.</summary>
public sealed class GetMessagesByConversationQueryValidatorTests
{
    private readonly GetMessagesByConversationQueryValidator _validator = new();

    [Fact]
    public void Deve_aceitar_parametros_validos()
    {
        var query = new GetMessagesByConversationQuery(Guid.NewGuid(), Guid.NewGuid(), 1, 50);

        _validator.Validate(query).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Deve_recusar_pagina_invalida(int page)
    {
        var query = new GetMessagesByConversationQuery(Guid.NewGuid(), Guid.NewGuid(), page, 50);

        _validator.Validate(query).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Deve_recusar_tamanho_de_pagina_acima_do_teto()
    {
        // Sem teto, `?pageSize=1000000` faria o serviço materializar a tabela
        // inteira em memória — um vetor de negação de serviço trivial de
        // explorar em qualquer API paginada.
        var query = new GetMessagesByConversationQuery(Guid.NewGuid(), Guid.NewGuid(), 1, 1_000_000);

        _validator.Validate(query).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Deve_exigir_o_identificador_do_solicitante()
    {
        // Sem solicitante não há como decidir autorização. Torná-lo obrigatório
        // no validador é a segunda barreira; a primeira é o próprio tipo do
        // record, que não permite omiti-lo.
        var query = new GetMessagesByConversationQuery(Guid.NewGuid(), Guid.Empty, 1, 50);

        _validator.Validate(query).IsValid.ShouldBeFalse();
    }
}
