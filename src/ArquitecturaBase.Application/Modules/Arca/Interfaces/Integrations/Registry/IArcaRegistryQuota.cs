namespace ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Registry;

/// <summary>
/// El cupo de cada persona para el padrón de un tercero: las CUIT distintas que consultó en el último minuto, hasta
/// <c>Arca:Registry:LookupsPerMinute</c>. Repetir una CUIT no gasta cupo: la vista previa de «¿La emito?», que se vuelve a pedir
/// con cada cambio, la sigue consultando. Lo que corre sin persona, como la emisión de fondo, no tiene cupo: un cupo nunca
/// rechaza una factura. Vive en memoria y por host, como los límites de pedidos de la Api.
/// </summary>
public interface IArcaRegistryQuota
{
    /// <summary>True si la persona puede consultar esa CUIT ahora; la cuenta como consultada.</summary>
    bool Admit(Guid? operatorId, string cuit);
}
