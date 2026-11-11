using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// How someone corrects and voids (decision 8): <see cref="Any"/> with «Corregir y anular respuestas» and «Ver respuestas»;
/// otherwise only what they loaded, with «Completar formularios». <see cref="CanRead"/> says whether someone else's response is
/// forbidden or does not exist for them.
/// </summary>
internal sealed record FormRecordAmendAccess(Guid Actor, bool Any, bool CanRead);

internal sealed class FormRecordAmendmentGuard(ICurrentUser currentUser, IPermissionService permissions, IUserReader users)
{
    public async Task<Result<FormRecordAmendAccess>> CheckAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor) return FormDefinitionErrors.Unauthorized;
        return await AccessAsync(actor, cancellationToken) is { } access ? access : FormDefinitionErrors.Forbidden;
    }

    /// <summary>How the person reading a response could change it, if at all; the detail uses it to offer the actions.</summary>
    public async Task<FormRecordAmendAccess?> FindAsync(CancellationToken cancellationToken) =>
        currentUser.UserId is { } actor ? await AccessAsync(actor, cancellationToken) : null;

    /// <summary>
    /// Whether this person changes this response now. Any current response with the permission; their own only while nobody
    /// decided it. A replaced or voided one conflicts; someone else's is forbidden, or missing for whoever only reads their own.
    /// </summary>
    public static Result Allows(FormRecordAmendAccess access, Guid recordActor, FormRecordState state, bool replaced)
    {
        if (!access.Any && recordActor != access.Actor) return access.CanRead ? FormDefinitionErrors.Forbidden : FormRecordErrors.NotFound;
        if (replaced || state == FormRecordState.Voided) return FormRecordErrors.AmendUnavailable;
        return access.Any || state is FormRecordState.Received or FormRecordState.PendingApproval
            ? Result.Success() : FormRecordErrors.AmendUnavailable;
    }

    public Task<bool> HasPermissionAsync(Guid actor, string permission, CancellationToken cancellationToken) =>
        permissions.HasPermissionAsync(actor, permission, cancellationToken);

    /// <summary>The person still has the same way in: losing the permission is not taken as having their own response.</summary>
    public async Task<bool> StillAllowedAsync(FormRecordAmendAccess access, CancellationToken cancellationToken) =>
        currentUser.UserId == access.Actor && await AccessAsync(access.Actor, cancellationToken) is { } current && (current.Any || !access.Any);

    private async Task<FormRecordAmendAccess?> AccessAsync(Guid actor, CancellationToken cancellationToken)
    {
        if (await users.FindByIdAsync(actor, cancellationToken) is not { IsActive: true }) return null;
        var read = await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.RecordsRead, cancellationToken);
        if (read && await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.RecordsAmend, cancellationToken))
            return new(actor, true, true);
        return await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.RecordsCreate, cancellationToken)
            ? new(actor, false, read) : null;
    }
}
