using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Infrastructure.WhatsApp;

/// <summary>
/// Con WhatsApp prendido y sin los dos secretos del webhook, avisa al arrancar que el webhook está apagado y qué falta
/// para prenderlo. Sin esto, el primer síntoma sería que Meta no puede verificar la URL. Nombra las claves, nunca un
/// valor.
/// </summary>
internal sealed partial class WhatsAppWebhookOffNotice(ILogger<WhatsAppWebhookOffNotice> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        LogWebhookOff(logger);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The WhatsApp webhook is off: WhatsApp:AppSecret and WhatsApp:VerifyToken are missing. Sending messages "
            + "still works, but /webhooks/whatsapp is not mapped and incoming messages are not received. To turn it on, "
            + "load both with dotnet user-secrets in src/ArquitecturaBase.Api and restart the Api")]
    private static partial void LogWebhookOff(ILogger logger);
}
