using ArquitecturaBase.Application.Interfaces.Integrations;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ArquitecturaBase.Infrastructure.WhatsApp;

/// <summary>
/// "whatsapp" en la readiness (<c>/health</c>). Con el token rechazado o sin permisos queda Degraded y no Unhealthy: el
/// resto de la app sigue sirviendo y la respuesta sigue siendo 200. La descripción dice cuál de los dos fue y que,
/// después de arreglarlo, hay que reiniciar la Api: las opciones se leen al arrancar. Va sin el tag "live": un reinicio
/// automático, sin un token nuevo, no arregla nada.
/// </summary>
internal sealed class WhatsAppHealthCheck(IWhatsAppAvailability availability, WhatsAppHealth health) : IHealthCheck
{
    public const string Name = "whatsapp";

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var result = !availability.IsEnabled
            ? HealthCheckResult.Healthy("disabled")
            : health.TokenProblem switch
            {
                null => HealthCheckResult.Healthy(),
                WhatsAppSendFailure.InvalidToken => HealthCheckResult.Degraded(
                    "WhatsApp rejected the access token. Load a new one in WhatsApp:AccessToken and restart the Api, "
                    + "which reads it at startup."),
                // MissingPermission: WhatsAppHealth no acepta otro motivo.
                _ => HealthCheckResult.Degraded(
                    "The WhatsApp access token lacks a permission. Give the system user the WhatsApp account and a token "
                    + "with whatsapp_business_messaging in WhatsApp:AccessToken, then restart the Api, which reads it at startup."),
            };

        return Task.FromResult(result);
    }
}
