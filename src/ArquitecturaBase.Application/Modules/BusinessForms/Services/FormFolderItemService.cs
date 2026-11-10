using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;
using P = ArquitecturaBase.Domain.Modules.BusinessForms.BusinessFormsPermissions;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// «Carpetas…» of a form or a catalog (CP2-A). Reading takes the read permission of the item; saving also takes its manage permission
/// and replaces the whole set in one transaction under the folders lock, only if it is still the one that was read.
/// </summary>
internal sealed class FormFolderItemService(ICurrentUser currentUser, IPermissionService permissions, IUserReader users,
    IFormFolderRepository repository, IFormFolderReader reader, IUnitOfWork unitOfWork, IRequestValidator validator,
    ILogger<FormFolderItemService> logger) : IFormFolderItemService
{
    public Task<Result<FormItemFoldersResponse>> GetAsync(FormFolderItemKind kind, Guid itemId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormItemFoldersResponse>>(logger, "GetFormItemFolders", async () =>
        {
            if (currentUser.UserId is not { } actor) return FormDefinitionErrors.Unauthorized;
            if (!await CanAsync(actor, kind, false, cancellationToken)) return FormDefinitionErrors.Forbidden;
            if (!await reader.ExistsItemAsync(kind, itemId, cancellationToken)) return NotFound(kind);
            return new FormItemFoldersResponse(await reader.ListItemFolderIdsAsync(kind, itemId, cancellationToken));
        });

    public Task<Result<FormItemFoldersResponse>> SetAsync(FormFolderItemKind kind, Guid itemId, SetFormItemFoldersRequest request,
        CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormItemFoldersResponse>>(logger, "SetFormItemFolders", async () =>
        {
            ArgumentNullException.ThrowIfNull(request);
            if (currentUser.UserId is not { } actor) return FormDefinitionErrors.Unauthorized;
            if (!await CanAsync(actor, kind, true, cancellationToken)) return FormDefinitionErrors.Forbidden;
            if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
            return await unitOfWork.ExecuteInTransactionAsync<Result<FormItemFoldersResponse>>(async inner =>
            {
                await repository.LockAsync(inner);
                if (!await CanAsync(actor, kind, true, inner)) return FormDefinitionErrors.Forbidden;
                if (!await reader.ExistsItemAsync(kind, itemId, inner)) return NotFound(kind);
                var folders = (await repository.ListAsync(inner)).Select(folder => folder.Id).ToHashSet();
                if (!request.FolderIds!.All(folders.Contains)) return FormFolderErrors.NotFound;
                var memberships = await repository.ListMembershipsAsync(kind, itemId, inner);
                var plan = FormFolderTree.PlanMemberships(memberships.Select(value => value.FolderId).ToArray(), request.ExpectedFolderIds!, request.FolderIds!);
                if (plan.IsFailure) return plan.Error;
                foreach (var removed in memberships.Where(value => plan.Value.Removed.Contains(value.FolderId))) repository.Remove(removed);
                foreach (var added in plan.Value.Added) repository.Add(BusinessFormFolderMembership.Create(added, kind, itemId));
                return new FormItemFoldersResponse(request.FolderIds!.ToArray());
            }, CommitPolicy.OnSuccess, cancellationToken);
        });

    private static Error NotFound(FormFolderItemKind kind) => kind == FormFolderItemKind.Form ? FormDefinitionErrors.NotFound : FormCatalogErrors.NotFound;

    private async Task<bool> CanAsync(Guid actor, FormFolderItemKind kind, bool write, CancellationToken cancellationToken)
    {
        if (await users.FindByIdAsync(actor, cancellationToken) is not { IsActive: true }) return false;
        var (read, manage) = kind == FormFolderItemKind.Form ? (P.Read, P.Manage) : (P.CatalogsRead, P.CatalogsManage);
        return await permissions.HasPermissionAsync(actor, read, cancellationToken)
            && (!write || await permissions.HasPermissionAsync(actor, manage, cancellationToken));
    }
}
