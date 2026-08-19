using System.Text.Json;
using BuildingBlocks.Contracts;
using IdentityService.Application.Abstractions;
using IdentityService.Domain;
using IdentityService.Infrastructure.Persistence;

namespace IdentityService.Infrastructure.Messaging;

/// <summary>
/// Grava eventos de integração na tabela de outbox usando o mesmo
/// <see cref="IdentityDbContext"/> da operação de negócio.
/// </summary>
/// <remarks>
/// O ponto central: recebe por injeção <b>a mesma instância</b> de contexto que o
/// repositório (ambos registrados como <c>Scoped</c>). É isso que faz a linha da
/// outbox e a linha do usuário entrarem na mesma transação. Se este writer
/// abrisse um contexto próprio, seriam duas transações independentes e o padrão
/// perderia completamente a razão de existir.
/// </remarks>
public sealed class EfOutboxWriter(IdentityDbContext dbContext) : IOutboxWriter
{
    /// <summary>
    /// Opções de serialização em camelCase, iguais às usadas pela API.
    /// </summary>
    /// <remarks>
    /// Estático e reaproveitado: <c>JsonSerializerOptions</c> monta um cache de
    /// metadados por instância. Criar uma nova a cada chamada descarta esse cache
    /// e é um gargalo de desempenho bem conhecido em .NET.
    /// </remarks>
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public void Add(IIntegrationEvent integrationEvent)
    {
        var eventType = integrationEvent.GetType();

        var outboxMessage = OutboxMessage.Create(
            // Reaproveita o EventId para que o mesmo identificador siga do
            // produtor até a inbox do consumidor, viabilizando a deduplicação.
            id: integrationEvent.EventId,
            type: eventType.Name,
            // Serializar pelo tipo CONCRETO (e não pela interface) é obrigatório:
            // passar o tipo estático `IIntegrationEvent` faria o serializador
            // emitir apenas os membros da interface, perdendo todos os campos do
            // evento. É um erro silencioso — o JSON sai válido, só que vazio.
            payload: JsonSerializer.Serialize(integrationEvent, eventType, SerializerOptions),
            occurredOnUtc: integrationEvent.OccurredAtUtc);

        // Apenas marca para inserção. O INSERT acontece no SaveChangesAsync do
        // handler, junto com as demais alterações.
        dbContext.OutboxMessages.Add(outboxMessage);
    }
}
