using System.ComponentModel.DataAnnotations;

namespace ArquitecturaBase.Api.RateLimiting;

/// <summary>Límites por IP de /account (sección 5.3), en la sección RateLimiting.</summary>
internal sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Pedidos de código: 20 cada 15 minutos.</summary>
    [Range(1, 100_000)]
    public int LoginCodePermitLimit { get; init; } = 20;

    [Range(1, 1440)]
    public int LoginCodeWindowMinutes { get; init; } = 15;

    /// <summary>Verificaciones: el spec no fija el número; 30 cada 15 minutos alcanza para varios intentos por código.</summary>
    [Range(1, 100_000)]
    public int LoginVerifyPermitLimit { get; init; } = 30;

    [Range(1, 1440)]
    public int LoginVerifyWindowMinutes { get; init; } = 15;
}
