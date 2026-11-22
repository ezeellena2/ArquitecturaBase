using System.ComponentModel.DataAnnotations;

namespace ArquitecturaBase.Application.Configuration.Auth;

/// <summary>
/// N16: cómo corre la retención de los datos del ingreso, de la sección <c>IdentityRetention</c> (identidad.md). Los plazos de
/// los ingresos, los códigos y las invitaciones ya no están acá: los elige el administrador en Configuración general ›
/// «Privacidad y retención» y la retención los lee de la base en cada corrida (RC1-A, 2026-11-22). Se valida al arrancar.
/// </summary>
public sealed class IdentityRetentionOptions
{
    public const string SectionName = "IdentityRetention";

    /// <summary>Cuántos días después de vencer se podan las autorizaciones y los tokens de OpenIddict.</summary>
    [Range(1, 3650, ErrorMessage = "IdentityRetention:SessionDaysAfterExpiry must be between {1} and {2}.")]
    public int SessionDaysAfterExpiry { get; init; } = 14;

    /// <summary>
    /// Cuántas filas de cada tabla borra o limpia un lote, cada uno en su transacción. Como mucho mil: los enlaces del lote
    /// toman un lock por cuenta, y la tabla de locks de PostgreSQL es chica (64 por conexión).
    /// </summary>
    [Range(1, 1000, ErrorMessage = "IdentityRetention:BatchSize must be between {1} and {2}.")]
    public int BatchSize { get; init; } = 500;

    /// <summary>Cada cuántas horas corre, después de la primera vez, que es al arrancar.</summary>
    [Range(1, 168, ErrorMessage = "IdentityRetention:IntervalHours must be between {1} and {2}.")]
    public int IntervalHours { get; init; } = 24;

    /// <summary>
    /// Si corre sola, en segundo plano. La apagan los tests de integración, que comparten la base y la llaman cuando
    /// quieren; fuera de los tests queda prendida.
    /// </summary>
    public bool RunInBackground { get; init; } = true;
}
