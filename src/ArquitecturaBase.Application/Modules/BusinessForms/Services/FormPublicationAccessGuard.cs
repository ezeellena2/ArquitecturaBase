using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormPublicationAccessGuard(ICurrentUser currentUser, IPermissionService permissions)
{
    public async Task<Result<Guid>> CheckAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor) return FormDefinitionErrors.Unauthorized;
        return await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.Read, cancellationToken)
            && await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.Publish, cancellationToken)
            ? actor : FormDefinitionErrors.Forbidden;
    }

}
