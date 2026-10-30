using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>Loading in the app: an active account with «Completar formularios», checked before reading and again before answering.</summary>
internal sealed class FormEntryAccessGuard(ICurrentUser currentUser, IPermissionService permissions, IUserReader users)
{
    public async Task<Result<Guid>> CheckAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor) return FormDefinitionErrors.Unauthorized;
        return await StillAllowedAsync(actor, cancellationToken) ? actor : FormDefinitionErrors.Forbidden;
    }

    public async Task<bool> StillAllowedAsync(Guid actor, CancellationToken cancellationToken) =>
        currentUser.UserId == actor && await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.RecordsCreate, cancellationToken)
            && await users.FindByIdAsync(actor, cancellationToken) is { IsActive: true };
}
