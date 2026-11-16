using System.ComponentModel.DataAnnotations;

namespace ArquitecturaBase.Api.RateLimiting;

/// <summary>
/// Los límites de todos los pedidos, en la sección RequestLimits (N6): la ventana por IP de las rutas anónimas sin límite
/// propio, los pedidos en curso de la copia y el tope del cuerpo. Cada copia cuenta los suyos.
/// </summary>
internal sealed class RequestLimitsOptions
{
    public const string SectionName = "RequestLimits";

    /// <summary>Pedidos a rutas anónimas sin política propia, por IP (IPv6 por su /64): 300 por minuto.</summary>
    [Range(1, 1_000_000)]
    public int AnonymousPermitLimit { get; init; } = 300;

    [Range(1, 3600)]
    public int AnonymousWindowSeconds { get; init; } = 60;

    /// <summary>Pedidos a la vez en la copia, salvo /health y /alive; los que pasan esperan en la cola, y sin lugar, 429.</summary>
    [Range(1, 100_000)]
    public int ConcurrentRequests { get; init; } = 100;

    [Range(0, 100_000)]
    public int QueuedRequests { get; init; } = 100;

    /// <summary>
    /// El cuerpo más grande que acepta Kestrel: 1 MiB. Una ruta que sube archivos declara el suyo con
    /// <c>[RequestSizeLimit]</c> (y <c>[RequestFormLimits]</c>), que manda sobre este.
    /// </summary>
    [Range(typeof(long), "1024", "1073741824")]
    public long MaxRequestBodyBytes { get; init; } = 1024 * 1024;
}
