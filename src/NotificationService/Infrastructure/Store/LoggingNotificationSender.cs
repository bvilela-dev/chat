using Microsoft.Extensions.Logging;
using NotificationService.Application.Abstractions;

namespace NotificationService.Infrastructure.Store;

/// <summary>
/// Implementação de demonstração que apenas registra as notificações em log.
/// </summary>
/// <remarks>
/// <para>
/// <b>É um stub explícito, não um esquecimento.</b> Integrar Firebase, APNs ou
/// um provedor de e-mail exigiria credenciais, contas e configuração que fogem
/// ao escopo desta demonstração — e que não acrescentariam nada
/// arquiteturalmente.
/// </para>
/// <para>
/// O que importa é que o ponto de extensão está definido: substituir esta classe
/// por uma <c>FirebaseNotificationSender</c> é alterar uma linha no registro de
/// dependências, sem tocar em nenhuma regra de negócio. É precisamente para isso
/// que a inversão de dependência existe.
/// </para>
/// </remarks>
public sealed class LoggingNotificationSender(ILogger<LoggingNotificationSender> logger) : INotificationSender
{
    /// <inheritdoc />
    public Task SendPushAsync(Guid userId, string message, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "[PUSH SIMULADO] usuário {UserId}: {Message}",
            userId,
            message);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendEmailAsync(Guid userId, string subject, string message, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "[E-MAIL SIMULADO] usuário {UserId}, assunto \"{Subject}\": {Message}",
            userId,
            subject,
            message);

        return Task.CompletedTask;
    }
}
