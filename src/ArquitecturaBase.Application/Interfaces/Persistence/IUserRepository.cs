using ArquitecturaBase.Application.Common.Exceptions;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Interfaces.Persistence;

/// <summary>
/// Escrituras de cuentas sobre Identity. Solo escribe datos: para leer, <see cref="IUserReader"/>; para el bloqueo, la
/// cookie y el cierre de sesiones, <see cref="Integrations.Identity.ISignInService"/>. Las escrituras de datos no renuevan el
/// security stamp (el alta pone el primero): cortar el acceso lo decide el caso de uso con <c>AccountAccessRevoker</c>.
/// Todas, igual que <see cref="LockExternalSignInAsync"/>, exigen la transacción del caso de uso
/// (<see cref="IUnitOfWork.ExecuteInTransactionAsync{TResult}"/>): sin ella lanzan <see cref="InvalidOperationException"/>
/// antes de tocar nada, también cuando un test prepara datos. UserManager guarda en cada operación, dentro de esa
/// transacción y con un savepoint por guardado: un Result fallido con <see cref="CommitPolicy.OnSuccess"/> o una
/// excepción deshacen todo, incluidos esos guardados. Un nombre más largo que <c>AccountRules.DisplayNameMaxLength</c>
/// lanza <see cref="ArgumentException"/>: HTTP ya lo valida, y un nombre ajeno (Google, WhatsApp) se recorta antes con
/// <c>AccountRules.FitExternalDisplayName</c>.
/// </summary>
public interface IUserRepository
{
    /// <summary>
    /// Serializa el alta o vínculo de una identidad externa por correo y clave del proveedor: toma external-login: y
    /// login-code: del correo en una sola llamada (por el orden ordinal, external-login: primero). Corre dentro de la
    /// transacción del caso de uso, que el ingreso con Google abre con <see cref="CommitPolicy.OnAnyResult"/>: la
    /// auditoría se confirma aunque el ingreso falle. Exige esa transacción; sin ella lanza
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    Task LockExternalSignInAsync(
        Email email, string provider, string providerKey, CancellationToken cancellationToken);

    /// <summary>
    /// Crea la cuenta con un correo, un número o los dos; sin ninguno lanza una <see cref="ArgumentException"/>, porque
    /// que haya al menos uno lo valida Application antes. El UserName es el Id de la cuenta, así cambiar el correo o el
    /// número no cambia nada más. El correo queda confirmado, porque ambos ingresos lo verifican; el número, según
    /// <paramref name="phoneConfirmed"/>: uno que carga un administrador queda sin verificar hasta que la persona entra con
    /// él. Le asigna el rol Admin si el correo es el configurado en Seed:AdminEmail, y User si no. Si Identity o la base lo
    /// rechazan lanza una excepción: el caso de uso buscó antes la cuenta por su correo y su número, así que un rechazo es
    /// un error de programación.
    /// </summary>
    Task<UserAccount> CreateAsync(
        Email? email,
        PhoneNumber? phone,
        bool phoneConfirmed,
        string? displayName,
        string culture,
        CancellationToken cancellationToken);

    /// <summary>
    /// El alta de un administrador (sección 12 del spec del ingreso con WhatsApp): como <see cref="CreateAsync"/>, pero el
    /// correo y el número quedan sin verificar hasta que la persona entra con ellos, porque nadie probó todavía que sean
    /// suyos. El ingreso con el código, Google y el bot los verifican. El alta toma el lock del destino, igual que el
    /// ingreso con código y Google (login-code: del correo); el que crea cuentas sin él es el bot, que solo tiene la fila
    /// del contacto. Si otra cuenta se quedó con el correo o el número entre la búsqueda del alta y este guardado, lanza
    /// <see cref="UniqueConstraintViolationException"/> y no queda nada de la cuenta, ni en la base ni para guardar
    /// después: el savepoint deshizo solo ese guardado y la transacción del caso de uso sigue usable. Cualquier otro
    /// rechazo sigue siendo un error de programación, como en <see cref="CreateAsync"/>.
    /// </summary>
    Task<UserAccount> CreateUnverifiedAsync(
        Email? email,
        PhoneNumber? phone,
        string? displayName,
        string culture,
        CancellationToken cancellationToken);

    /// <summary>Vincula el proveedor externo a la cuenta.</summary>
    Task AddExternalLoginAsync(Guid userId, ExternalLogin login, CancellationToken cancellationToken);

    /// <summary>Deshace el borrado lógico, deja la cuenta activa y le pone el nombre del alta.</summary>
    Task RestoreAsync(Guid userId, string? displayName, CancellationToken cancellationToken);

    /// <summary>
    /// Le pone el correo a la cuenta, verificado o no, y recalcula el normalizado que usa la búsqueda por correo. Como
    /// <see cref="SetPhoneAsync"/>, solo escribe el dato y no renueva el security stamp. El correo tiene índice único:
    /// quien llama se fija antes con <see cref="IUserReader.FindByEmailAsync"/> e
    /// <see cref="IUserReader.ExistsDeletedByEmailAsync"/>, y un choque posterior se trata igual que con el número.
    /// </summary>
    Task SetEmailAsync(Guid userId, Email email, bool confirmed, CancellationToken cancellationToken);

    /// <summary>
    /// Le pone el número a la cuenta, verificado o no. Solo escribe el dato: no renueva el security stamp, que le cortaría
    /// la cookie a quien vincula su propio número desde el perfil. Si hay que cortar el acceso, lo decide quien llama con
    /// <c>AccountAccessRevoker</c>, que además de cerrar las sesiones invalida los enlaces pendientes. El número tiene
    /// índice único: quien llama se fija antes con <see cref="IUserReader.FindByPhoneAsync"/> e
    /// <see cref="IUserReader.ExistsDeletedByPhoneAsync"/>. Si igual choca, porque otra cuenta lo guardó entre esa búsqueda y
    /// este guardado, lanza <see cref="UniqueConstraintViolationException"/> y la cuenta queda como estaba: la
    /// transacción sigue usable, y con <see cref="CommitPolicy.OnAnyResult"/> se confirma lo demás.
    /// </summary>
    Task SetPhoneAsync(Guid userId, PhoneNumber phone, bool confirmed, CancellationToken cancellationToken);

    /// <summary>
    /// Le saca el número a la cuenta y lo deja sin verificar. Como <see cref="SetPhoneAsync"/>, solo escribe el dato:
    /// cuando lo desvincula un administrador, el caso de uso corta el acceso con <c>AccountAccessRevoker</c>.
    /// </summary>
    Task RemovePhoneAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Deja la cuenta exactamente con esos roles: agrega los que faltan y saca los que sobran.</summary>
    Task SetRolesAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken);

    Task SetDisplayNameAsync(Guid userId, string? displayName, CancellationToken cancellationToken);

    Task SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken);

    /// <summary>Borrado lógico: la fila queda y el filtro global la esconde, así el historial sigue existiendo.</summary>
    Task DeleteAsync(Guid userId, CancellationToken cancellationToken);

    Task UpdateProfileAsync(
        Guid userId, string? displayName, string culture, string timeZoneId, CancellationToken cancellationToken);
}
