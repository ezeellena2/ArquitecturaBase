using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormCatalogAccessGuard(ICurrentUser currentUser, IPermissionService permissions)
{
    public Task<bool> HasPermissionAsync(Guid actor, string permission, CancellationToken cancellationToken) =>
        permissions.HasPermissionAsync(actor, permission, cancellationToken);

    public async Task<Result<Guid>> CheckAsync(bool writing, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor) return FormDefinitionErrors.Unauthorized;
        if (!await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.CatalogsRead, cancellationToken)
            || (writing && !await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.CatalogsManage, cancellationToken)))
            return FormDefinitionErrors.Forbidden;
        return actor;
    }
}
