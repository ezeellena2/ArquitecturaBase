using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormLifecycleAccessGuard(ICurrentUser currentUser, IPermissionService permissions, IRequestValidator validator)
{
    public Task<Result<Guid>> CheckAsync(CancellationToken ct) => CheckPermissionAsync(BusinessFormsPermissions.Publish, ct);
    public Task<Result<Guid>> CheckDeletionAsync(CancellationToken ct) => CheckPermissionAsync(BusinessFormsPermissions.Manage, ct);
    private async Task<Result<Guid>> CheckPermissionAsync(string permission, CancellationToken ct)
    {
        if (currentUser.UserId is not {} actor) return FormDefinitionErrors.Unauthorized;
        return await StillAllowedPermissionAsync(actor, permission, ct) ? actor : FormDefinitionErrors.Forbidden;
    }
    public Task<bool> StillAllowedAsync(Guid actor, CancellationToken ct) => StillAllowedPermissionAsync(actor, BusinessFormsPermissions.Publish, ct);
    public Task<bool> StillAllowedDeletionAsync(Guid actor, CancellationToken ct) => StillAllowedPermissionAsync(actor, BusinessFormsPermissions.Manage, ct);
    private async Task<bool> StillAllowedPermissionAsync(Guid actor, string permission, CancellationToken ct) => currentUser.UserId == actor
        && await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.Read, ct)
        && await permissions.HasPermissionAsync(actor, permission, ct);
    public Task<ValidationError?> ValidateAsync<TRequest>(TRequest request, CancellationToken ct) => validator.ValidateAsync(request, ct);
    public async Task<bool> CanReadRecordsAsync(Guid actor, CancellationToken ct) => currentUser.UserId == actor
        && await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.RecordsRead, ct);
    public async Task<bool> CanManageAsync(Guid actor, CancellationToken ct) => currentUser.UserId == actor
        && await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.Manage, ct);
}
