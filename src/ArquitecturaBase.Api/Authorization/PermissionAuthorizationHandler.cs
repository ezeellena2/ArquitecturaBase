using ArquitecturaBase.Application.Abstractions.Identity;
using Microsoft.AspNetCore.Authorization;

namespace ArquitecturaBase.Api.Authorization;

/// <summary>El usuario cumple si alguno de sus roles tiene el permiso. Los permisos no viajan en el token.</summary>
internal sealed class PermissionAuthorizationHandler(ICurrentUser currentUser, IPermissionService permissionService)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var cancellationToken = context.Resource is HttpContext httpContext ? httpContext.RequestAborted : CancellationToken.None;

        if (currentUser.UserId is { } userId
            && await permissionService.HasPermissionAsync(userId, requirement.Permission, cancellationToken))
        {
            context.Succeed(requirement);
        }
    }
}
