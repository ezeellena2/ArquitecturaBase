namespace ArquitecturaBase.Application.Interfaces.Persistence;

/// <summary>
/// La lectura de los permisos efectivos (N9 y N33, 2026-11-16): sin caché, de la base, en el contexto del pedido. Adentro de
/// un límite corre en su conexión y su transacción, sin pedir otra al pool.
/// </summary>
public interface IPermissionReader
{
    /// <summary>
    /// Los permisos de los roles de la cuenta, cada uno una vez y en una sola consulta: sus claims de permiso, sin los de
    /// otro tipo. Sin filtrar por el catálogo, que es de <c>IPermissionService</c>.
    /// </summary>
    Task<IReadOnlyCollection<string>> ListPermissionsForUserAsync(Guid userId, CancellationToken cancellationToken);
}
