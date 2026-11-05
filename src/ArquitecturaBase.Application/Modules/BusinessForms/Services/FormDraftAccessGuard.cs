using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormDraftAccessGuard(ICurrentUser currentUser, IPermissionService permissions)
{
    public Task<bool> HasPermissionAsync(Guid actor, string permission, CancellationToken cancellationToken) =>
        permissions.HasPermissionAsync(actor, permission, cancellationToken);

    public async Task<Result<Guid>> CheckAsync(bool writing, CancellationToken cancellationToken) =>
        currentUser.UserId is { } actor ? await CheckActorAsync(actor, writing, cancellationToken) : FormDefinitionErrors.Unauthorized;

    // The same permissions for a named account, such as the person of a channel proposal.
    public async Task<Result<Guid>> CheckActorAsync(Guid actor, bool writing, CancellationToken cancellationToken)
    {
        if (!await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.Read, cancellationToken)
            || (writing && !await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.Manage, cancellationToken)))
            return FormDefinitionErrors.Forbidden;
        return actor;
    }

    // Read+Manage+CatalogsRead: the person preview and the editor maximum return what the requested catalog offers.
    public async Task<Result<Guid>> CheckRecipientPreviewAsync(CancellationToken cancellationToken)
    {
        var allowed = await CheckAsync(true, cancellationToken);
        if (allowed.IsFailure) return allowed;
        return await permissions.HasPermissionAsync(allowed.Value, BusinessFormsPermissions.CatalogsRead, cancellationToken) ? allowed : FormDefinitionErrors.Forbidden;
    }
}
