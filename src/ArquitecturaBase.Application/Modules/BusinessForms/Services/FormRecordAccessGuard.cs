using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal sealed class FormRecordAccessGuard(ICurrentUser currentUser, IPermissionService permissions)
{
    public async Task<Result<Guid>> CheckAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor) return FormDefinitionErrors.Unauthorized;
        return await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.RecordsRead, cancellationToken)
            ? actor : FormDefinitionErrors.Forbidden;
    }
    public async Task<Result<Guid>> CheckExportAsync(CancellationToken cancellationToken)
    {
        var actor = await CheckAsync(cancellationToken); if (actor.IsFailure) return actor.Error;
        return await permissions.HasPermissionAsync(actor.Value, BusinessFormsPermissions.RecordsExport, cancellationToken) ? actor.Value : FormDefinitionErrors.Forbidden;
    }
    public async Task<bool> StillAllowedAsync(Guid actor, CancellationToken cancellationToken, bool export = false) =>
        currentUser.UserId == actor && await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.RecordsRead, cancellationToken)
            && (!export || await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.RecordsExport, cancellationToken));
}
