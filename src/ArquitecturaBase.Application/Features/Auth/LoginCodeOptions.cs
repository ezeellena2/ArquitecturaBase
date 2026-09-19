using System.ComponentModel.DataAnnotations;

namespace ArquitecturaBase.Application.Features.Auth;

/// <summary>Reglas del código de ingreso (sección 5.3 del spec), en Authentication:LoginCode.</summary>
public sealed class LoginCodeOptions
{
    public const string SectionName = "Authentication:LoginCode";

    [Range(4, 10)]
    public int Length { get; init; } = 6;

    [Range(1, 60)]
    public int LifetimeMinutes { get; init; } = 10;

    [Range(1, 20)]
    public int MaxAttempts { get; init; } = 5;

    [Range(0, 3600)]
    public int ResendCooldownSeconds { get; init; } = 60;

    [Range(1, 100)]
    public int MaxRequestsPerWindow { get; init; } = 5;

    [Range(1, 1440)]
    public int RequestWindowMinutes { get; init; } = 15;

    /// <summary>Verificaciones fallidas seguidas que bloquean la cuenta (bloqueo de Identity).</summary>
    [Range(1, 100)]
    public int LockoutMaxFailedAttempts { get; init; } = 10;

    [Range(1, 1440)]
    public int LockoutMinutes { get; init; } = 15;
}
