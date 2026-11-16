using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.Roles;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.Services.Users;

/// <summary>
/// Deja la historia de un cambio administrativo (N13 de la revisión del núcleo): una fila de
/// <see cref="AdministrativeChange"/> por dato que cambió, con quién pide y la hora. Como <c>LoginAuditRecorder</c>, sólo
/// agrega filas: las guarda el límite del caso de uso, que la llama al final de su trabajo, después de la última regla
/// que puede rechazarlo. Un cambio deshecho no deja historia, y uno que no cambia nada tampoco. Del correo y del número
/// guarda el estado (<see cref="AdministrativeChange.ContactState"/>), nunca el dato. El seed escribe las suyas sin actor,
/// desde Infrastructure.
/// </summary>
internal sealed class AdministrativeChangeRecorder(
    IAdministrativeChangeRepository changes,
    ICurrentUser currentUser,
    TimeProvider timeProvider)
{
    public void RoleCreated(Guid roleId, string name, string? description, IEnumerable<string> permissions) =>
        Add(AdministrativeChangeKind.RoleCreated, roleId, name, added: permissions, after: description);

    /// <summary>De <paramref name="before"/> a lo que se guardó: el nombre, la descripción y los permisos, cada uno si cambió.</summary>
    public void RoleUpdated(
        RoleRow before, string name, string? description, IReadOnlyCollection<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(permissions);

        if (!string.Equals(before.Name, name, StringComparison.Ordinal))
        {
            Add(AdministrativeChangeKind.RoleRenamed, before.Id, name, before: before.Name, after: name);
        }

        if (!string.Equals(before.Description, description, StringComparison.Ordinal))
        {
            Add(AdministrativeChangeKind.RoleDescriptionChanged, before.Id, name, before: before.Description, after: description);
        }

        string[] added = [.. permissions.Except(before.Permissions, StringComparer.Ordinal)];
        string[] removed = [.. before.Permissions.Except(permissions, StringComparer.Ordinal)];
        if (added.Length > 0 || removed.Length > 0)
        {
            Add(AdministrativeChangeKind.RolePermissionsChanged, before.Id, name, added, removed);
        }
    }

    /// <summary>El rol borrado, con los permisos que tenía: el borrado es físico y se los lleva.</summary>
    public void RoleDeleted(RoleRow role)
    {
        ArgumentNullException.ThrowIfNull(role);

        Add(AdministrativeChangeKind.RoleDeleted, role.Id, role.Name, removed: role.Permissions, before: role.Description);
    }

    /// <summary>Los roles que la cuenta recibió y perdió; sin ninguno, nada.</summary>
    public void RolesChanged(Guid userId, IReadOnlyCollection<string> added, IReadOnlyCollection<string> removed)
    {
        ArgumentNullException.ThrowIfNull(added);
        ArgumentNullException.ThrowIfNull(removed);

        if (added.Count > 0 || removed.Count > 0)
        {
            Add(AdministrativeChangeKind.UserRolesChanged, userId, added: added, removed: removed);
        }
    }

    /// <summary>Activar o desactivar <paramref name="before"/>; si ya estaba así, nada.</summary>
    public void StatusChanged(UserAccount before, bool isActive)
    {
        ArgumentNullException.ThrowIfNull(before);

        if (before.IsActive != isActive)
        {
            Add(AdministrativeChangeKind.UserStatusChanged, before.Id,
                before: AdministrativeChange.Status(before.IsActive), after: AdministrativeChange.Status(isActive));
        }
    }

    public void Deleted(UserAccount before)
    {
        ArgumentNullException.ThrowIfNull(before);

        Add(AdministrativeChangeKind.UserDeleted, before.Id,
            before: AdministrativeChange.Status(before.IsActive), after: AdministrativeChange.Deleted);
    }

    /// <summary>
    /// El alta que restauró la cuenta borrada: vuelve activa con <paramref name="roles"/>, todos, aunque ya los tuviera
    /// antes de borrarse; lo que cambió respecto de esos va en <see cref="RolesChanged"/>, y el correo y el número del alta
    /// (N4), en <see cref="ContactChanged"/>.
    /// </summary>
    public void Restored(Guid userId, IReadOnlyCollection<string> roles) =>
        Add(AdministrativeChangeKind.UserRestored, userId, added: roles,
            before: AdministrativeChange.Deleted, after: AdministrativeChange.Active);

    /// <summary>
    /// El correo y el número con los que quedó <paramref name="after"/>, comparados con <paramref name="before"/>: cada uno
    /// deja su fila si cambió el dato o su verificación. Sólo el estado, nunca el dato.
    /// </summary>
    public void ContactChanged(UserAccount before, UserAccount after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        if (!string.Equals(before.Email, after.Email, StringComparison.OrdinalIgnoreCase)
            || before.EmailConfirmed != after.EmailConfirmed)
        {
            EmailChanged(before, after.Email is not null, after.EmailConfirmed);
        }

        if (!string.Equals(before.PhoneNumber, after.PhoneNumber, StringComparison.Ordinal)
            || before.PhoneNumberConfirmed != after.PhoneNumberConfirmed)
        {
            PhoneChanged(before, after.PhoneNumber is not null, after.PhoneNumberConfirmed);
        }
    }

    /// <summary>El correo de <paramref name="before"/> pasa a otro (o a ninguno): sólo el estado, nunca el correo.</summary>
    public void EmailChanged(UserAccount before, bool present, bool verified)
    {
        ArgumentNullException.ThrowIfNull(before);

        Add(AdministrativeChangeKind.UserEmailChanged, before.Id,
            before: AdministrativeChange.ContactState(before.Email is not null, before.EmailConfirmed),
            after: AdministrativeChange.ContactState(present, verified));
    }

    /// <summary>El número de <paramref name="before"/> pasa a otro (o a ninguno): sólo el estado, nunca el número.</summary>
    public void PhoneChanged(UserAccount before, bool present, bool verified)
    {
        ArgumentNullException.ThrowIfNull(before);

        Add(AdministrativeChangeKind.UserPhoneChanged, before.Id,
            before: AdministrativeChange.ContactState(before.PhoneNumber is not null, before.PhoneNumberConfirmed),
            after: AdministrativeChange.ContactState(present, verified));
    }

    /// <summary>El modo de registro de la fila <paramref name="settingsId"/>; si no cambió, nada.</summary>
    public void RegistrationModeChanged(Guid settingsId, RegistrationMode before, RegistrationMode after)
    {
        if (before != after)
        {
            Add(AdministrativeChangeKind.RegistrationModeChanged, settingsId,
                before: before.ToString(), after: after.ToString());
        }
    }

    private void Add(
        AdministrativeChangeKind kind,
        Guid targetId,
        string? targetName = null,
        IEnumerable<string>? added = null,
        IEnumerable<string>? removed = null,
        string? before = null,
        string? after = null) =>
        changes.Add(AdministrativeChange.Create(
            kind,
            currentUser.UserId,
            targetId,
            timeProvider.GetUtcNow().UtcDateTime,
            targetName,
            added,
            removed,
            before,
            after));
}
