using System.ComponentModel.DataAnnotations;

namespace ArquitecturaBase.Application.Configuration.Auth;

/// <summary>
/// N16: cuánto se conservan los datos del ingreso, de la sección <c>IdentityRetention</c> (decisión del 2026-11-14,
/// identidad.md). Los valores por defecto son los decididos y valen solos; se validan al arrancar.
/// </summary>
public sealed class IdentityRetentionOptions
{
    public const string SectionName = "IdentityRetention";

    /// <summary>
    /// A los cuántos días se borran los códigos y los enlaces de ingreso. Los límites que leen <c>LoginCodes</c> miran 24
    /// horas como mucho, así que un día ya los cubre.
    /// </summary>
    [Range(1, 3650, ErrorMessage = "IdentityRetention:LoginCodeDays must be between {1} and {2}.")]
    public int LoginCodeDays { get; init; } = 30;

    /// <summary>A los cuántos meses un registro de ingreso queda sin la IP ni el navegador.</summary>
    [Range(1, 120, ErrorMessage = "IdentityRetention:LoginAuditMonths must be between {1} and {2}.")]
    public int LoginAuditMonths { get; init; } = 12;

    /// <summary>
    /// A los cuántos días se borra una invitación que ya no es la última de su cuenta. La última queda siempre: es la que
    /// muestra «Editar usuario». Los topes de las invitaciones miran 24 horas.
    /// </summary>
    // Como mínimo 7: con menos, el plazo se acercaría al día que vive la entrega de una invitación
    // (QueuedDelivery.DefaultLifetime), y se podría borrar una invitación cuya entrega todavía está en curso.
    [Range(7, 3650, ErrorMessage = "IdentityRetention:InvitationDays must be between {1} and {2}.")]
    public int InvitationDays { get; init; } = 30;

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
