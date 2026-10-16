using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormApprovalAccessGuard(ICurrentUser currentUser, IPermissionService permissions, IFormApprovalReader reader)
{
    public async Task<Result<Guid>> CheckAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor) return FormDefinitionErrors.Unauthorized;
        return await StillAllowedAsync(actor, cancellationToken) ? actor : FormDefinitionErrors.Forbidden;
    }
    public async Task<bool> StillAllowedAsync(Guid actor, CancellationToken cancellationToken) => currentUser.UserId == actor
        && await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.ApprovalsDecide, cancellationToken)
        && await reader.FindActorAsync(actor, null, cancellationToken) is { IsActive: true };
}
