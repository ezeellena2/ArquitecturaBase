using Microsoft.AspNetCore.Authorization;

namespace ArquitecturaBase.Api.Authorization;

internal sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}
