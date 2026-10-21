using Microsoft.AspNetCore.Authorization;

namespace ArquitecturaBase.Api.Authorization;

internal sealed class PermissionRequirement(IReadOnlyList<string> permissions) : IAuthorizationRequirement
{
    public IReadOnlyList<string> Permissions { get; } = permissions;
}
