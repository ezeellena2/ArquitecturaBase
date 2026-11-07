using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using Microsoft.AspNetCore.Authorization;

namespace ArquitecturaBase.Api.Authorization;

/// <summary>El usuario cumple si alguno de sus roles tiene el permiso. Los permisos no viajan en el token.</summary>
internal sealed class PermissionAuthorizationHandler(ICurrentUser currentUser, IPermissionService permissionService)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var cancellationToken = context.Resource is HttpContext httpContext ? httpContext.RequestAborted : CancellationToken.None;

        if (currentUser.UserId is not { } userId)
        {
            return;
        }

        // Una lectura por requisito: preguntar permiso por permiso repetiría la consulta de roles por cada alternativa.
        var granted = await permissionService.GetPermissionsAsync(userId, cancellationToken);

        if (requirement.Permissions.Any(permission => granted.Contains(permission)))
        {
            context.Succeed(requirement);
        }
    }
}
