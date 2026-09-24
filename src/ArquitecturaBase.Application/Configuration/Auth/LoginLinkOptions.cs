using System.ComponentModel.DataAnnotations;

namespace ArquitecturaBase.Application.Configuration.Auth;

/// <summary>
/// Límites de los enlaces de ingreso por cuenta (sección 13 del spec del ingreso con WhatsApp), en
/// Authentication:LoginLink. Los mismos nombres que en <see cref="LoginCodeOptions"/>. Cuánto dura un enlace no se
/// configura: son los 10 minutos de <c>LoginLink.Lifetime</c>.
/// </summary>
public sealed class LoginLinkOptions
{
    public const string SectionName = "Authentication:LoginLink";

    /// <summary>Cuánto hay que esperar entre un enlace y el siguiente de la misma cuenta.</summary>
    [Range(0, 3600)]
    public int ResendCooldownSeconds { get; init; } = 60;

    [Range(1, 100)]
    public int MaxRequestsPerWindow { get; init; } = 5;

    [Range(1, 1440)]
    public int RequestWindowMinutes { get; init; } = 15;
}
