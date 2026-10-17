using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal sealed class FormSuggestionAccessGuard(ICurrentUser currentUser, IPermissionService permissions)
{
    public async Task<Result<Guid>> CheckAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor) return FormDefinitionErrors.Unauthorized;
        return await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.SuggestionsManage, cancellationToken) ? actor : FormDefinitionErrors.Forbidden;
    }
    public async Task<bool> StillAllowedAsync(Guid actor, CancellationToken cancellationToken) => currentUser.UserId == actor
        && await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.SuggestionsManage, cancellationToken);
}
