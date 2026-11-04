using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Application.Modules.Arca.Interfaces.Channels.Connection;

/// <summary>
/// Una familia de servicios de ARCA (padrón, facturación, granos…) presentada a la conexión, que no la nombra (ADR 0025).
/// Es un puerto de la forma «todos avisados»: cada familia suma su adaptador, en <c>Channels/&lt;Familia&gt;</c>, con
/// <c>TryAddEnumerable</c>. El adaptador no abre transacciones: lo que la familia guarde lo guarda su propio servicio.
/// </summary>
public interface IArcaServiceDefinition
{
    /// <summary>Los servicios de ARCA de la familia, con su código WSN.</summary>
    IReadOnlyCollection<ArcaInstalledService> Services { get; }

    /// <summary>
    /// Prueba un servicio elegido con el ticket que le da el contexto, y guarda lo que la familia necesite. Nunca lanza
    /// por una respuesta de ARCA: la devuelve en el resultado.
    /// </summary>
    Task<ArcaServiceCheckResponse> CheckAsync(ArcaServiceCode service, ArcaServiceContext context, CancellationToken cancellationToken);

    /// <summary>Si la configuración propia de la familia está completa (Facturación: un punto de venta elegido).</summary>
    Task<bool> IsCompleteAsync(Guid connectionId, CancellationToken cancellationToken);
}
