using System.ComponentModel.DataAnnotations;

namespace ArquitecturaBase.Api.Modules.Arca;

/// <summary>Pedidos por minuto y por operador. Lo que llama a ARCA (la prueba, subir el certificado y los puntos de venta) tiene el más bajo.</summary>
public sealed class ArcaRateLimitOptions
{
    [Range(1, 100000)] public int ReadPermitLimit { get; set; } = 120;
    [Range(1, 100000)] public int WritePermitLimit { get; set; } = 20;
    [Range(1, 100000)] public int RemotePermitLimit { get; set; } = 6;
}
