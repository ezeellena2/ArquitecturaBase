using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormSuggestionHistoryAccessGuard(IUserReader users, IPermissionService permissions)
{
    public async Task<bool> CanReadAsync(Guid? actorUserId, CancellationToken ct) =>
        actorUserId is not { } actor || actor != Guid.Empty
        && await users.FindByIdAsync(actor, ct) is { IsActive: true }
        && await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.RecordsRead, ct);
}
