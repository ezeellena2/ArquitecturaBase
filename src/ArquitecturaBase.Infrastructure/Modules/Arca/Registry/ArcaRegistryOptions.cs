using System.ComponentModel.DataAnnotations;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Registry;

/// <summary>
/// El padrón de un tercero (<c>Arca:Registry</c>): cuántas CUIT distintas puede consultar cada persona por minuto. Son 6 por
/// defecto, como lo que llama a ARCA desde sus rutas (<c>Arca:RateLimiting:RemotePermitLimit</c>).
/// </summary>
public sealed class ArcaRegistryOptions
{
    [Range(1, 100000)] public int LookupsPerMinute { get; set; } = 6;
}
