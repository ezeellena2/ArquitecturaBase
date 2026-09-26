using Microsoft.AspNetCore.Authorization;

namespace ArquitecturaBase.Api.Authorization;

/// <summary>
/// Pide un permiso de <c>Permissions</c> para entrar a un controller o a una acción. Arma el nombre de la política que
/// resuelve <see cref="PermissionPolicyProvider"/>: sin sesión responde 401 y sin el permiso en alguno de los roles,
/// 403. Las rutas piden permisos, nunca roles.
/// </summary>
public sealed class HasPermissionAttribute(string permission)
    : AuthorizeAttribute(PermissionPolicyProvider.PolicyPrefix + permission)
{
    public string Permission { get; } = permission;
}
