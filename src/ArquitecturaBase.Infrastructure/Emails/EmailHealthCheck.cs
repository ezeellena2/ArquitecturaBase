using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ArquitecturaBase.Infrastructure.Emails;

/// <summary>
/// N5: "email" en la readiness (<c>/health</c>). Con las credenciales rechazadas o el servidor sin responder queda Degraded y no
/// Unhealthy: el resto de la app sigue sirviendo, la respuesta sigue siendo 200 y Container Apps no saca la copia del tráfico.
/// Mientras tanto, los correos esperan en la cola y se reintentan hasta que vencen. Va sin el tag "live": un reinicio no
/// arregla una contraseña revocada.
/// </summary>
internal sealed class EmailHealthCheck(EmailHealth health) : IHealthCheck
{
    public const string Name = "email";

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(health.Problem switch
        {
            null => HealthCheckResult.Healthy(),
            EmailHealthProblem.CredentialsRejected => HealthCheckResult.Degraded(
                "The mail server rejected the credentials. Load a new app password in Email:Smtp:Password and restart the Api, "
                + "which reads it at startup; meanwhile the emails wait in the queue until they expire."),
            _ => HealthCheckResult.Degraded(
                "The mail server could not be reached, refused the last attempts for now or refused the sender (as a spent "
                + "daily sending quota); the emails it did not take for now wait in the queue and are retried until they expire, "
                + "and those it refused for good are dropped."),
        });
}
