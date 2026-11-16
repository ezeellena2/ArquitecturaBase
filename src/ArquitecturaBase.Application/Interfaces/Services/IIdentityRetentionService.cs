using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Services;

/// <summary>
/// N16: la retención de los datos del ingreso (identidad.md, «Retención de los datos del ingreso»). La corre el worker de
/// Infrastructure: lotes mientras alguno sale lleno y, al final, la poda de OpenIddict, tramo por tramo. Cada llamada es
/// una transacción que toma primero el lock de la retención sin esperar: si otra copia de la Api lo tiene, no hace nada y
/// devuelve que no queda nada, y la corrida termina.
/// </summary>
public interface IIdentityRetentionService
{
    /// <summary>
    /// Un lote de cada paso: borra los códigos, los enlaces y las invitaciones viejas y deja sin la IP ni el navegador (y,
    /// si fallaron, sin lo que la persona escribió) los ingresos viejos. True si algún paso llenó el lote: puede haber más.
    /// </summary>
    Task<Result<bool>> PurgeBatchAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Un tramo de la poda de las autorizaciones y los tokens de OpenIddict vencidos hace más del plazo, desde
    /// <paramref name="fromUtc"/> (null en el primero). Devuelve desde dónde sigue el próximo, o null si no quedan.
    /// </summary>
    Task<Result<DateTime?>> PruneSessionsTrancheAsync(DateTime? fromUtc, CancellationToken cancellationToken);
}
