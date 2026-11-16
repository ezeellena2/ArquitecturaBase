namespace ArquitecturaBase.Domain.Users;

/// <summary>
/// Qué dato cambió en un <see cref="AdministrativeChange"/>. Se guarda como texto, como el método de ingreso en
/// LoginAudits: se lee de una consulta SQL sin una tabla de códigos. Un valor nuevo se escribe un despliegue después del
/// que ya sabe leerlo (N27).
/// </summary>
public enum AdministrativeChangeKind
{
    /// <summary>Un rol nuevo: <c>Added</c>, sus permisos; <c>After</c>, su descripción.</summary>
    RoleCreated,

    /// <summary>El nombre de un rol: <c>Before</c> y <c>After</c>.</summary>
    RoleRenamed,

    /// <summary>La descripción de un rol: <c>Before</c> y <c>After</c>.</summary>
    RoleDescriptionChanged,

    /// <summary>Los permisos de un rol: <c>Added</c>, los que recibió; <c>Removed</c>, los que perdió.</summary>
    RolePermissionsChanged,

    /// <summary>Un rol borrado: <c>Removed</c>, los permisos que tenía; <c>Before</c>, su descripción.</summary>
    RoleDeleted,

    /// <summary>Los roles de una cuenta: <c>Added</c>, los que recibió; <c>Removed</c>, los que perdió.</summary>
    UserRolesChanged,

    /// <summary>Activar o desactivar una cuenta: <c>Before</c> y <c>After</c>, <c>Active</c> o <c>Inactive</c>.</summary>
    UserStatusChanged,

    /// <summary>Una cuenta borrada: <c>Before</c>, <c>Active</c> o <c>Inactive</c>; <c>After</c>, <c>Deleted</c>.</summary>
    UserDeleted,

    /// <summary>
    /// Una cuenta restaurada por un alta: <c>Added</c>, todos los roles con los que vuelve; <c>Before</c>, <c>Deleted</c>;
    /// <c>After</c>, <c>Active</c>. Lo que cambió respecto de los roles que tenía va en <see cref="UserRolesChanged"/>, y el
    /// correo y el número del alta, en <see cref="UserEmailChanged"/> y <see cref="UserPhoneChanged"/>.
    /// </summary>
    UserRestored,

    /// <summary>
    /// El correo de una cuenta, sin el correo: <c>Before</c> y <c>After</c>, <c>None</c>, <c>Unverified</c> o
    /// <c>Verified</c>.
    /// </summary>
    UserEmailChanged,

    /// <summary>El número de una cuenta, sin el número: <c>Before</c> y <c>After</c>, como el correo.</summary>
    UserPhoneChanged,

    /// <summary>El modo de registro: <c>Before</c> (vacío en el seed) y <c>After</c>.</summary>
    RegistrationModeChanged,
}
