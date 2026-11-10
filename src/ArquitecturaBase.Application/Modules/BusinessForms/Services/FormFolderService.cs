using System.Globalization;
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
/// The folder tree of each listing (CP1-A) and its administration (CP3-A, CP5-A). Folders only order: reading the tree takes the
/// permission of its listing, and whoever does not administer folders sees only those with something visible for them, with their
/// parents. Every change is one transaction under the folders lock, where the revision, the levels, the cycles and the names are
/// checked against the whole tree.
/// </summary>
internal sealed class FormFolderService(ICurrentUser currentUser, IPermissionService permissions, IUserReader users,
    IFormFolderRepository repository, IFormFolderReader reader, IUnitOfWork unitOfWork, IRequestValidator validator,
    ILogger<FormFolderService> logger) : IFormFolderService
{
    public Task<Result<FormFolderTreeResponse>> ListAsync(ListFormFoldersRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormFolderTreeResponse>>(logger, "ListFormFolders", async () =>
        {
            ArgumentNullException.ThrowIfNull(request);
            if (currentUser.UserId is not { } actor) return FormDefinitionErrors.Unauthorized;
            if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
            var listing = request.Kind switch { "forms" => FormFolderListing.Forms, "designs" => FormFolderListing.Designs, _ => FormFolderListing.Catalogs };
            if (await users.FindByIdAsync(actor, cancellationToken) is not { IsActive: true }) return FormDefinitionErrors.Forbidden;
            var canReadRecords = await HasAsync(actor, P.RecordsRead, cancellationToken);
            var canDecide = await HasAsync(actor, P.ApprovalsDecide, cancellationToken);
            var canCreate = await HasAsync(actor, P.RecordsCreate, cancellationToken);
            var allowed = listing switch
            {
                FormFolderListing.Forms => canReadRecords || canDecide || canCreate,
                FormFolderListing.Designs => await HasAsync(actor, P.Read, cancellationToken),
                _ => await HasAsync(actor, P.CatalogsRead, cancellationToken),
            };
            if (!allowed) return FormDefinitionErrors.Forbidden;
            var canManage = await HasAsync(actor, P.Manage, cancellationToken) || await HasAsync(actor, P.CatalogsManage, cancellationToken);
            var folders = await reader.ListAsync(cancellationToken);
            if (!canManage)
            {
                var visible = Ancestors(folders, await reader.ListFolderIdsWithItemsAsync(listing, actor, canReadRecords, canDecide, canCreate, cancellationToken));
                folders = folders.Where(folder => visible.Contains(folder.Id)).ToArray();
            }
            return new FormFolderTreeResponse(Alphabetical(folders).Select(Response).ToArray(), canManage);
        });

    public Task<Result<FormFolderResponse>> CreateAsync(CreateFormFolderRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormFolderResponse>>(logger, "CreateFormFolder", async () =>
        {
            ArgumentNullException.ThrowIfNull(request);
            if (currentUser.UserId is not { } actor) return FormDefinitionErrors.Unauthorized;
            if (!await CanManageAsync(actor, cancellationToken)) return FormDefinitionErrors.Forbidden;
            if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
            return await unitOfWork.ExecuteInTransactionAsync<Result<FormFolderResponse>>(async inner =>
            {
                await repository.LockAsync(inner);
                if (!await CanManageAsync(actor, inner)) return FormDefinitionErrors.Forbidden;
                var folders = await repository.ListAsync(inner);
                var created = BusinessFormFolder.Create(request.ParentId, request.Name);
                if (created.IsFailure) return FieldErrors.On(created.Error, "name");
                var placement = FormFolderTree.CheckPlacement(null, request.ParentId, created.Value.NormalizedName, Nodes(folders));
                if (placement.IsFailure) return Placement(placement.Error);
                repository.Add(created.Value);
                return Response(created.Value);
            }, CommitPolicy.OnSuccess, cancellationToken);
        });

    public Task<Result<FormFolderResponse>> UpdateAsync(Guid id, UpdateFormFolderRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormFolderResponse>>(logger, "UpdateFormFolder", async () =>
        {
            ArgumentNullException.ThrowIfNull(request);
            if (currentUser.UserId is not { } actor) return FormDefinitionErrors.Unauthorized;
            if (!await CanManageAsync(actor, cancellationToken)) return FormDefinitionErrors.Forbidden;
            if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
            return await unitOfWork.ExecuteInTransactionAsync<Result<FormFolderResponse>>(async inner =>
            {
                await repository.LockAsync(inner);
                if (!await CanManageAsync(actor, inner)) return FormDefinitionErrors.Forbidden;
                var folders = await repository.ListAsync(inner);
                if (folders.SingleOrDefault(folder => folder.Id == id) is not { } folder) return FormFolderErrors.NotFound;
                if (folder.CheckRevision(request.ExpectedRevision!.Value) is { IsFailure: true } stale) return stale.Error;
                var placement = FormFolderTree.CheckPlacement(id, request.ParentId, BusinessFormFolder.NormalizeName(request.Name!.Trim()), Nodes(folders));
                if (placement.IsFailure) return Placement(placement.Error);
                var updated = folder.Update(request.ExpectedRevision.Value, request.Name, request.ParentId);
                return updated.IsFailure ? FieldErrors.On(updated.Error, "name") : Response(folder);
            }, CommitPolicy.OnSuccess, cancellationToken);
        });

    public Task<Result<FormFolderDeletionResponse>> GetDeletionAsync(Guid id, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormFolderDeletionResponse>>(logger, "GetFormFolderDeletion", async () =>
        {
            if (currentUser.UserId is not { } actor) return FormDefinitionErrors.Unauthorized;
            if (!await CanManageAsync(actor, cancellationToken)) return FormDefinitionErrors.Forbidden;
            var folders = await reader.ListAsync(cancellationToken);
            if (folders.SingleOrDefault(folder => folder.Id == id) is not { } folder) return FormFolderErrors.NotFound;
            var subtree = FormFolderTree.Subtree(id, Nodes(folders));
            var counts = await reader.CountItemsAsync(subtree.ToArray(), cancellationToken);
            return new FormFolderDeletionResponse(folder.Revision, subtree.Count - 1, counts.Forms, counts.Catalogs);
        });

    public Task<Result> DeleteAsync(Guid id, DeleteFormFolderRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result>(logger, "DeleteFormFolder", async () =>
        {
            ArgumentNullException.ThrowIfNull(request);
            if (currentUser.UserId is not { } actor) return FormDefinitionErrors.Unauthorized;
            if (!await CanManageAsync(actor, cancellationToken)) return FormDefinitionErrors.Forbidden;
            if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
            return await unitOfWork.ExecuteInTransactionAsync<Result>(async inner =>
            {
                await repository.LockAsync(inner);
                if (!await CanManageAsync(actor, inner)) return FormDefinitionErrors.Forbidden;
                var folders = await repository.ListAsync(inner);
                if (folders.SingleOrDefault(folder => folder.Id == id) is not { } folder) return FormFolderErrors.NotFound;
                if (folder.CheckRevision(request.ExpectedRevision!.Value) is { IsFailure: true } stale) return stale;
                // Its subfolders and every membership go with it; no form or catalog is ever deleted.
                repository.Remove(folder);
                return Result.Success();
            }, CommitPolicy.OnSuccess, cancellationToken);
        });

    public Task<Result<FormFolderResponse>> SetMenuAsync(Guid id, SetFormFolderMenuRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormFolderResponse>>(logger, "SetFormFolderMenu", async () =>
        {
            ArgumentNullException.ThrowIfNull(request);
            if (currentUser.UserId is not { } actor) return FormDefinitionErrors.Unauthorized;
            if (!await CanManageAsync(actor, cancellationToken)) return FormDefinitionErrors.Forbidden;
            if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
            var placement = Enum.Parse<FormFolderMenuPlacement>(request.Placement!);
            return await unitOfWork.ExecuteInTransactionAsync<Result<FormFolderResponse>>(async inner =>
            {
                await repository.LockAsync(inner);
                if (!await CanManageAsync(actor, inner)) return FormDefinitionErrors.Forbidden;
                var folders = await repository.ListAsync(inner);
                if (folders.SingleOrDefault(folder => folder.Id == id) is not { } folder) return FormFolderErrors.NotFound;
                var set = folder.SetMenu(request.ExpectedRevision!.Value, placement, request.Icon);
                if (set.IsFailure) return set.Error == FormFolderErrors.IconInvalid ? FieldErrors.On(set.Error, "icon") : set.Error;
                return Response(folder);
            }, CommitPolicy.OnSuccess, cancellationToken);
        });

    internal static FormFolderNode[] Nodes(IEnumerable<BusinessFormFolder> folders) =>
        folders.Select(folder => new FormFolderNode(folder.Id, folder.ParentId, folder.NormalizedName)).ToArray();

    internal static FormFolderNode[] Nodes(IEnumerable<FormFolderRow> folders) =>
        folders.Select(folder => new FormFolderNode(folder.Id, folder.ParentId, folder.NormalizedName)).ToArray();

    internal static IEnumerable<T> Alphabetical<T>(IEnumerable<T> items, Func<T, string> name, Func<T, Guid> id)
    {
        var comparer = StringComparer.Create(CultureInfo.CurrentUICulture, CompareOptions.IgnoreCase);
        return items.OrderBy(name, comparer).ThenBy(id);
    }

    private static IEnumerable<FormFolderRow> Alphabetical(IEnumerable<FormFolderRow> folders) => Alphabetical(folders, folder => folder.Name, folder => folder.Id);

    // The folders with something visible and every folder above them, so the tree stays whole.
    private static HashSet<Guid> Ancestors(IReadOnlyList<FormFolderRow> folders, IReadOnlyList<Guid> direct)
    {
        var parents = folders.ToDictionary(folder => folder.Id, folder => folder.ParentId);
        var visible = new HashSet<Guid>();
        foreach (var id in direct)
            for (Guid? current = id; current is { } value && parents.ContainsKey(value) && visible.Add(value); current = parents[value]) { }
        return visible;
    }

    private static Error Placement(Error error) =>
        error == FormFolderErrors.NameTaken ? FieldErrors.On(error, "name")
        : error == FormFolderErrors.TooDeep || error == FormFolderErrors.Cycle ? FieldErrors.On(error, "parentId")
        : error;

    private static FormFolderResponse Response(BusinessFormFolder folder) =>
        new(folder.Id, folder.ParentId, folder.Name, folder.Revision, folder.MenuPlacement.ToString(), folder.MenuIcon);

    private static FormFolderResponse Response(FormFolderRow folder) =>
        new(folder.Id, folder.ParentId, folder.Name, folder.Revision, folder.MenuPlacement, folder.MenuIcon);

    private async Task<bool> CanManageAsync(Guid actor, CancellationToken cancellationToken) =>
        await users.FindByIdAsync(actor, cancellationToken) is { IsActive: true }
        && (await HasAsync(actor, P.Manage, cancellationToken) || await HasAsync(actor, P.CatalogsManage, cancellationToken));

    private Task<bool> HasAsync(Guid actor, string permission, CancellationToken cancellationToken) =>
        permissions.HasPermissionAsync(actor, permission, cancellationToken);
}
