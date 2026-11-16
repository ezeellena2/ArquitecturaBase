using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// La readiness (todos los checks) se calcula como mucho una vez por <see cref="Window"/> (N6): una ráfaga a /health no
/// le pide una conexión a la base ni un comando a Redis por pedido. Lo que llega mientras se calcula espera ese mismo
/// cálculo, y el resultado se guarda aunque sea malo, para que una base caída tampoco reciba una consulta por pedido. Una
/// consulta con filtro (la liveness, o un test que pide un check) no pasa por acá: corre siempre.
/// </summary>
internal sealed class CachedHealthCheckService(HealthCheckService inner, TimeProvider timeProvider) : HealthCheckService
{
    /// <summary>Menos que el período del probe de readiness (10 s), para que cada probe vea un resultado nuevo.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(2);

    private readonly Lock _gate = new();
    private Task<HealthReport>? _current;
    private DateTimeOffset _freshUntil;

    public override Task<HealthReport> CheckHealthAsync(
        Func<HealthCheckRegistration, bool>? predicate,
        CancellationToken cancellationToken = default)
    {
        if (predicate is not null)
        {
            return inner.CheckHealthAsync(predicate, cancellationToken);
        }

        Task<HealthReport> current;
        lock (_gate)
        {
            if (_current is null || (_current.IsCompleted && timeProvider.GetUtcNow() >= _freshUntil))
            {
                _current = EvaluateAsync();
            }

            current = _current;
        }

        // El cálculo es de todos: quien se va deja de esperarlo, pero no lo corta.
        return current.WaitAsync(cancellationToken);
    }

    private async Task<HealthReport> EvaluateAsync()
    {
        try
        {
            return await inner.CheckHealthAsync(null, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _freshUntil = timeProvider.GetUtcNow() + Window;
            }
        }
    }
}
