using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
/// <summary>Who reads responses: «Ver respuestas» reads them all; «Completar formularios» alone, only the person's own (CW6-C).</summary>
internal sealed record FormRecordReadAccess(Guid Actor, bool OwnOnly)
{
    public ListFormRecordsRequest Apply(ListFormRecordsRequest request) => OwnOnly ? request with { OwnerUserId = Actor } : request;
}
internal sealed class FormRecordAccessGuard(ICurrentUser currentUser, IPermissionService permissions, IFormResponseReader records)
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
    public async Task<Result<FormRecordReadAccess>> CheckReadAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor) return FormDefinitionErrors.Unauthorized;
        if (await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.RecordsRead, cancellationToken)) return new FormRecordReadAccess(actor, false);
        return await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.RecordsCreate, cancellationToken)
            ? new FormRecordReadAccess(actor, true) : FormDefinitionErrors.Forbidden;
    }
    /// <summary>Someone else's response does not exist for whoever only reads their own, not even by its address.</summary>
    public async Task<bool> CanReadAsync(FormRecordReadAccess access, Guid recordId, CancellationToken cancellationToken) =>
        !access.OwnOnly || await records.ExistsByActorAsync(recordId, access.Actor, cancellationToken);
    public async Task<bool> StillAllowedAsync(FormRecordReadAccess access, CancellationToken cancellationToken) =>
        currentUser.UserId == access.Actor && await permissions.HasPermissionAsync(access.Actor,
            access.OwnOnly ? BusinessFormsPermissions.RecordsCreate : BusinessFormsPermissions.RecordsRead, cancellationToken);
}
