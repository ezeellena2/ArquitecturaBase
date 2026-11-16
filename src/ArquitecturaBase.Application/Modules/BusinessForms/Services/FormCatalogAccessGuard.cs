using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormCatalogAccessGuard(ICurrentUser currentUser, IPermissionService permissions, IUserReader users)
{
    public Task<bool> HasPermissionAsync(Guid actor, string permission, CancellationToken cancellationToken) =>
        permissions.HasPermissionAsync(actor, permission, cancellationToken);

    public async Task<Result<Guid>> CheckAsync(bool writing, CancellationToken cancellationToken) =>
        currentUser.UserId is { } actor ? await CheckActorAsync(actor, writing, cancellationToken) : FormDefinitionErrors.Unauthorized;

    // The same permissions for a named account, such as the person of a channel proposal: it must also be an active account.
    public async Task<Result<Guid>> CheckActorAsync(Guid actor, bool writing, CancellationToken cancellationToken)
    {
        if (await users.FindByIdAsync(actor, cancellationToken) is not { IsActive: true }) return FormDefinitionErrors.Forbidden;
        if (!await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.CatalogsRead, cancellationToken)
            || (writing && !await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.CatalogsManage, cancellationToken)))
            return FormDefinitionErrors.Forbidden;
        return actor;
    }
}
