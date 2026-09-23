using ArquitecturaBase.Application.Abstractions.Phones;
using ArquitecturaBase.Application.Abstractions.WhatsApp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.WhatsApp;

/// <summary>
/// Manda los mensajes de la cola de a uno (sección 9 del spec). Lo que Meta rechaza por un rato (el límite por persona,
/// el de la cuenta, un 5xx o un timeout) se reintenta con espera, hasta 3 intentos en total; lo que fallaría igual (la
/// ventana de 24 horas, un número sin WhatsApp, el token o sus permisos) no se insiste. Un problema del token va además
/// a <see cref="WhatsAppHealth"/>. Los logs llevan el número enmascarado, el motivo y el código de Meta: nunca el cuerpo,
/// el código de ingreso, la URL ni el token.
/// </summary>
internal sealed partial class WhatsAppSenderBackgroundService(
    WhatsAppOutbox outbox,
    IServiceScopeFactory scopeFactory,
    IOptions<WhatsAppOptions> options,
    IPhoneNumberParser phoneNumberParser,
    WhatsAppHealth health,
    TimeProvider timeProvider,
    ILogger<WhatsAppSenderBackgroundService> logger)
    : BackgroundService
{
    public const int MaxAttempts = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in outbox.ReadAllAsync(stoppingToken))
        {
            await SendWithRetriesAsync(message, stoppingToken);
        }
    }

    private async Task SendWithRetriesAsync(WhatsAppOutboundMessage message, CancellationToken cancellationToken)
    {
        var messageType = message.GetType().Name;
        var recipient = phoneNumberParser.Mask(message.To);

        for (var attempt = 1; ; attempt++)
        {
            WhatsAppSendResult result;

            try
            {
                // Un scope por intento: el HttpClient tipado es transient y el factory rota sus handlers.
                await using var scope = scopeFactory.CreateAsyncScope();
                result = await scope.ServiceProvider.GetRequiredService<IWhatsAppCloudClient>().SendAsync(message, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // El cliente traduce las fallas de Meta y de la red: esto es un bug. Se descarta y la cola sigue.
                LogUnexpectedFailure(logger, messageType, recipient, exception);

                return;
            }

            if (result.IsSent)
            {
                health.ReportSent();
                LogSent(logger, messageType, recipient);

                return;
            }

            var failure = result.Failure!.Value;

            if (failure.IsTokenProblem())
            {
                // No es este mensaje: todos van a fallar igual hasta que alguien arregle el token y reinicie la Api.
                health.ReportTokenProblem(failure);

                if (failure == WhatsAppSendFailure.InvalidToken)
                {
                    LogInvalidToken(logger, messageType, recipient, result.MetaErrorCode);
                }
                else
                {
                    LogMissingPermission(logger, messageType, recipient, result.MetaErrorCode);
                }

                return;
            }

            if (!failure.IsRetryable())
            {
                LogNotSent(logger, messageType, recipient, failure, result.MetaErrorCode);

                return;
            }

            if (attempt == MaxAttempts)
            {
                LogDropped(logger, messageType, recipient, MaxAttempts, failure, result.MetaErrorCode);

                return;
            }

            // Nunca menos de 6 segundos (lo controla WhatsAppOptions al arrancar): antes, Meta rechaza otra vez un
            // mensaje a la misma persona (131056) y el intento se pierde.
            var delay = TimeSpan.FromSeconds(options.Value.RetryDelaySeconds);
            LogRetry(logger, messageType, recipient, failure, result.MetaErrorCode, attempt, MaxAttempts, delay.TotalSeconds);

            await Task.Delay(delay, timeProvider, cancellationToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The {MessageType} to {Recipient} was sent")]
    private static partial void LogSent(ILogger logger, string messageType, string recipient);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Sending the {MessageType} to {Recipient} failed with {Failure} (Meta code {MetaErrorCode}, attempt {Attempt} of {MaxAttempts}); retrying in {DelaySeconds} s")]
    private static partial void LogRetry(
        ILogger logger,
        string messageType,
        string recipient,
        WhatsAppSendFailure failure,
        int? metaErrorCode,
        int attempt,
        int maxAttempts,
        double delaySeconds);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The {MessageType} to {Recipient} was not sent: {Failure} (Meta code {MetaErrorCode})")]
    private static partial void LogNotSent(
        ILogger logger,
        string messageType,
        string recipient,
        WhatsAppSendFailure failure,
        int? metaErrorCode);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The {MessageType} to {Recipient} was dropped after {MaxAttempts} attempts: {Failure} (Meta code {MetaErrorCode})")]
    private static partial void LogDropped(
        ILogger logger,
        string messageType,
        string recipient,
        int maxAttempts,
        WhatsAppSendFailure failure,
        int? metaErrorCode);

    // Los dos de configuración dicen que hay que reiniciar: las opciones se leen al arrancar (IOptions), así que un
    // token nuevo o con más permisos no se usa hasta entonces.
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "WhatsApp rejected the access token (Meta code {MetaErrorCode}); the {MessageType} to {Recipient} was not sent. Load a new token in WhatsApp:AccessToken and restart the Api, which reads it at startup")]
    private static partial void LogInvalidToken(ILogger logger, string messageType, string recipient, int? metaErrorCode);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The WhatsApp access token lacks a permission (Meta code {MetaErrorCode}); the {MessageType} to {Recipient} was not sent. Give the system user the WhatsApp account and a token with whatsapp_business_messaging in WhatsApp:AccessToken, then restart the Api, which reads it at startup")]
    private static partial void LogMissingPermission(ILogger logger, string messageType, string recipient, int? metaErrorCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Sending the {MessageType} to {Recipient} failed unexpectedly; the message was dropped")]
    private static partial void LogUnexpectedFailure(ILogger logger, string messageType, string recipient, Exception exception);
}
