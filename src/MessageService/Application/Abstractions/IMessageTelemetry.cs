namespace MessageService.Application.Abstractions;

/// <summary>
/// Métricas de negócio do Message Service.
/// </summary>
/// <remarks>
/// Abstrair a telemetria mantém a camada de aplicação livre de
/// <c>System.Diagnostics.Metrics</c> e permite que os testes verifiquem que a
/// métrica certa foi registrada — sem precisar de um coletor de verdade.
/// </remarks>
public interface IMessageTelemetry
{
    /// <summary>Contabiliza um evento consumido, rotulado pelo nome do evento.</summary>
    void RecordConsumedEvent(string eventName);

    /// <summary>
    /// Registra o atraso entre o envio da mensagem e a atualização da projeção.
    /// </summary>
    /// <remarks>
    /// <b>É a métrica mais importante de um sistema CQRS.</b> Ela mede
    /// diretamente o tamanho da janela de consistência eventual — ou seja, por
    /// quanto tempo o usuário pode ver dados desatualizados. Um crescimento
    /// sustentado dessa latência indica que os consumidores não estão dando conta
    /// do volume, e é o sinal para escalar antes que o problema fique visível.
    /// </remarks>
    void RecordProjectionLag(TimeSpan lag);
}
