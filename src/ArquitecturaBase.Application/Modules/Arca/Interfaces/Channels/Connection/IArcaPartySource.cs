using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.Arca.Interfaces.Channels.Connection;

/// <summary>
/// El padrón de un tercero para las familias que emiten: completa un cliente desde su CUIT («Completar con ARCA» y «Otro:
/// escribir la CUIT»). Lo aporta el padrón, que las demás familias no nombran (ADR 0025); el adaptador va en
/// <c>Channels/Registry</c>, consulta en nombre de quien firma, sólo lee y no abre transacciones. Una CUIT inválida no llega a
/// ARCA.
/// </summary>
public interface IArcaPartySource
{
    Task<Result<ArcaParty>> FindAsync(string taxId, CancellationToken cancellationToken);
}
