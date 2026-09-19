using ArquitecturaBase.Application.Abstractions.Emails;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Emails;

/// <summary>
/// Envía los emails encolados de a uno, con hasta 3 intentos y espera exponencial. Si fallan todos, registra el
/// error y sigue: el usuario puede pedir otro código. El log nunca incluye el destinatario ni el contenido.
/// </summary>
internal sealed partial class EmailBackgroundService(
    EmailQueue queue,
    IServiceScopeFactory scopeFactory,
    IOptions<EmailOptions> options,
    TimeProvider timeProvider,
    ILogger<EmailBackgroundService> logger)
    : BackgroundService
{
    public const int MaxAttempts = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in queue.ReadAllAsync(stoppingToken))
        {
            await SendWithRetriesAsync(message, stoppingToken);
        }
    }

    private async Task SendWithRetriesAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IEmailSender>().SendAsync(message, cancellationToken);

                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (attempt == MaxAttempts)
                {
                    LogEmailDropped(logger, MaxAttempts, exception);

                    return;
                }

                LogEmailRetry(logger, attempt, MaxAttempts, exception);

                var delay = TimeSpan.FromSeconds(options.Value.RetryDelaySeconds * Math.Pow(2, attempt - 1));
                await Task.Delay(delay, timeProvider, cancellationToken);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sending an email failed (attempt {Attempt} of {MaxAttempts}); retrying")]
    private static partial void LogEmailRetry(ILogger logger, int attempt, int maxAttempts, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Sending an email failed after {MaxAttempts} attempts; the email was dropped")]
    private static partial void LogEmailDropped(ILogger logger, int maxAttempts, Exception exception);
}
