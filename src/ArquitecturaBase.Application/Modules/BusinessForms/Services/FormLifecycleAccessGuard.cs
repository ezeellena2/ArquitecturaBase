using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormLifecycleAccessGuard(ICurrentUser currentUser, IPermissionService permissions, IRequestValidator validator)
{
    public async Task<Result<Guid>> CheckAsync(CancellationToken ct)
    {
        if (currentUser.UserId is not {} actor) return FormDefinitionErrors.Unauthorized;
        return await StillAllowedAsync(actor, ct) ? actor : FormDefinitionErrors.Forbidden;
    }
    public async Task<bool> StillAllowedAsync(Guid actor, CancellationToken ct) => currentUser.UserId == actor
        && await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.Read, ct)
        && await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.Publish, ct);
    public Task<ValidationError?> ValidateAsync(DeactivateFormRequest request, CancellationToken ct) => validator.ValidateAsync(request, ct);
    public async Task<bool> CanReadRecordsAsync(Guid actor, CancellationToken ct) => currentUser.UserId == actor
        && await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.RecordsRead, ct);
}
