using ArquitecturaBase.Application.Modules.Arca.Models.Connection;

namespace ArquitecturaBase.Application.Modules.Arca.Interfaces.Channels.Connection;

/// <summary>
/// Los datos de la empresa emisora para las familias que emiten (la condición frente al IVA decide la clase del comprobante).
/// Los aporta el padrón, que las demás familias no nombran (ADR 0025); el adaptador va en <c>Channels/Registry</c>, sólo lee
/// y no abre transacciones. Null si el padrón todavía no trajo los datos.
/// </summary>
public interface IArcaCompanyProfileSource
{
    Task<ArcaCompanyProfile?> FindAsync(Guid connectionId, CancellationToken cancellationToken);
}
