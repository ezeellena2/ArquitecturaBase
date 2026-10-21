using Microsoft.AspNetCore.Authorization;

namespace ArquitecturaBase.Api.Authorization;

/// <summary>Pide sesión y al menos uno de los permisos indicados. Otros atributos se cumplen además de éste.</summary>
public sealed class HasAnyPermissionAttribute(params string[] permissions)
    : AuthorizeAttribute(PermissionPolicyProvider.AnyPolicyPrefix + string.Join(",", permissions))
{
    public IReadOnlyList<string> Permissions { get; } = Array.AsReadOnly(permissions.ToArray());
}
