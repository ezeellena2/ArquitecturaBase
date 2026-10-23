using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal sealed class FormPersonDataAccessGuard(ICurrentUser currentUser, IPermissionService permissions, IUserReader users)
{
    public async Task<Result> CheckAttributesAsync(bool writing, CancellationToken ct)
    {
        if (currentUser.UserId is not { } actor) return FormDefinitionErrors.Unauthorized;
        if (await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.PersonDataManage, ct)) return Result.Success();
        if (!writing && (await permissions.HasPermissionAsync(actor, Permissions.Users.Manage, ct)
            || await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.Manage, ct))) return Result.Success();
        return FormDefinitionErrors.Forbidden;
    }
    public async Task<Result> CheckPeopleAsync(CancellationToken ct)
    {
        if (currentUser.UserId is not { } actor) return FormDefinitionErrors.Unauthorized;
        return await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.PersonDataManage, ct)
            && await permissions.HasPermissionAsync(actor, Permissions.Users.Read, ct) ? Result.Success() : FormDefinitionErrors.Forbidden;
    }
    public async Task<Result> CheckValuesAsync(Guid userId, CancellationToken ct)
    {
        if (currentUser.UserId is not { } actor) return FormDefinitionErrors.Unauthorized;
        if (!await permissions.HasPermissionAsync(actor, Permissions.Users.Manage, ct)) return FormDefinitionErrors.Forbidden;
        return await users.FindByIdAsync(userId, ct) is null ? FormPersonDataErrors.UserNotFound : Result.Success();
    }
}
