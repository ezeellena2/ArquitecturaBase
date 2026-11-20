using System.Globalization;
using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;
using P = ArquitecturaBase.Domain.Modules.BusinessForms.BusinessFormsPermissions;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The folder tree of each listing (CP1-A) and its administration (CP3-A, CM1 to CM6). Folders only order: reading the tree takes the
/// permission of its listing; whoever administers forms or catalogs sees every folder (a catalog goes into any folder that exists) and
/// the rest only those with something visible for them, with their parents. Only whoever administers forms creates, renames, moves,
/// changes the icon of and deletes folders, each change in one transaction under the folders lock, where the revision, the two levels,
/// the cycles, the names and the icon of a menu are checked against the whole tree: the same two levels the tree shows
/// (<see cref="BusinessFormFolder.TwoLevels"/>), so a folder it does not show is not found. The same person chooses the forms directly in
/// a folder (CM2), saving the whole set against the one that was read.
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
            var canManage = await HasAsync(actor, P.Manage, cancellationToken);
            var folders = await reader.ListAsync(cancellationToken);
            if (!canManage && !await HasAsync(actor, P.CatalogsManage, cancellationToken))
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
                var folders = await TreeAsync(inner);
                var icon = request.ParentId is null ? request.Icon ?? BusinessFormFolder.FirstFreeIcon(folders) : request.Icon;
                var created = BusinessFormFolder.Create(request.ParentId, request.Name, icon);
                if (created.IsFailure) return Field(created.Error);
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
                var folders = await TreeAsync(inner);
                if (folders.SingleOrDefault(folder => folder.Id == id) is not { } folder) return FormFolderErrors.NotFound;
                if (folder.CheckRevision(request.ExpectedRevision!.Value) is { IsFailure: true } stale) return stale.Error;
                var placement = FormFolderTree.CheckPlacement(id, request.ParentId, BusinessFormFolder.NormalizeName(request.Name!.Trim()), Nodes(folders));
                if (placement.IsFailure) return Placement(placement.Error);
                // A menu keeps the icon it shows unless another comes; a subfolder that goes up takes the first free one, not one it hid.
                var icon = request.ParentId is null ? request.Icon ?? folder.ShownIcon ?? BusinessFormFolder.FirstFreeIcon(folders) : request.Icon;
                var updated = folder.Update(request.ExpectedRevision.Value, request.Name, request.ParentId, icon);
                return updated.IsFailure ? Field(updated.Error) : Response(folder);
            }, CommitPolicy.OnSuccess, cancellationToken);
        });

    public Task<Result<FormFolderDeletionResponse>> GetDeletionAsync(Guid id, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormFolderDeletionResponse>>(logger, "GetFormFolderDeletion", async () =>
        {
            if (currentUser.UserId is not { } actor) return FormDefinitionErrors.Unauthorized;
            if (!await CanManageAsync(actor, cancellationToken)) return FormDefinitionErrors.Forbidden;
            if ((await reader.ListAsync(cancellationToken)).SingleOrDefault(folder => folder.Id == id) is not { } folder) return FormFolderErrors.NotFound;
            // It counts everything the delete takes, also what the tree does not show below the folder.
            var counts = await reader.CountDeletionAsync(id, cancellationToken);
            return new FormFolderDeletionResponse(folder.Revision, counts.Subfolders, counts.Forms, counts.Catalogs);
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
                if ((await TreeAsync(inner)).SingleOrDefault(folder => folder.Id == id) is not { } folder) return FormFolderErrors.NotFound;
                if (folder.CheckRevision(request.ExpectedRevision!.Value) is { IsFailure: true } stale) return stale;
                // Its subfolders, also the ones the tree does not show, and every membership go with it; no form or catalog is ever deleted.
                repository.Remove(folder);
                return Result.Success();
            }, CommitPolicy.OnSuccess, cancellationToken);
        });

    public Task<Result<FormFolderFormsResponse>> GetFormsAsync(Guid id, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormFolderFormsResponse>>(logger, "GetFormFolderForms", async () =>
        {
            if (currentUser.UserId is not { } actor) return FormDefinitionErrors.Unauthorized;
            if (!await CanManageAsync(actor, cancellationToken)) return FormDefinitionErrors.Forbidden;
            if (!(await reader.ListAsync(cancellationToken)).Any(folder => folder.Id == id)) return FormFolderErrors.NotFound;
            return new FormFolderFormsResponse(await reader.ListFolderFormIdsAsync(id, cancellationToken));
        });

    // «Agregar formularios» (CM2) mirrors «Carpetas…» of a form: the same 404s, the same 409 and, for a form that would be in more
    // folders than a form can hold, the same refusal its own folders would get. The set read is compared first: one that changed because
    // a form was deleted in the meantime is a conflict, not a missing form.
    public Task<Result<FormFolderFormsResponse>> SetFormsAsync(Guid id, SetFormFolderFormsRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormFolderFormsResponse>>(logger, "SetFormFolderForms", async () =>
        {
            ArgumentNullException.ThrowIfNull(request);
            if (currentUser.UserId is not { } actor) return FormDefinitionErrors.Unauthorized;
            if (!await CanManageAsync(actor, cancellationToken)) return FormDefinitionErrors.Forbidden;
            if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
            return await unitOfWork.ExecuteInTransactionAsync<Result<FormFolderFormsResponse>>(async inner =>
            {
                await repository.LockAsync(inner);
                if (!await CanManageAsync(actor, inner)) return FormDefinitionErrors.Forbidden;
                if (!(await reader.ListAsync(inner)).Any(folder => folder.Id == id)) return FormFolderErrors.NotFound;
                // The current set leaves out the deleted forms, so their memberships stay as deleting them left them.
                var plan = FormFolderTree.PlanMemberships(await reader.ListFolderFormIdsAsync(id, inner), request.ExpectedFormIds!, request.FormIds!);
                if (plan.IsFailure) return plan.Error;
                var folderCounts = (await reader.ListFormFolderCountsAsync(request.FormIds!, inner)).ToDictionary(row => row.FormId, row => row.FolderCount);
                if (!request.FormIds!.All(folderCounts.ContainsKey)) return FormDefinitionErrors.NotFound;
                if (plan.Value.Added.Any(form => folderCounts[form] >= FormFolderRequestValidation.MaxFoldersPerItem))
                    return FieldErrors.Validation("formIds", FormFolderRequestValidation.Invalid());
                var memberships = await repository.ListFolderMembershipsAsync(id, FormFolderItemKind.Form, inner);
                foreach (var removed in memberships.Where(value => plan.Value.Removed.Contains(value.ItemId))) repository.Remove(removed);
                foreach (var added in plan.Value.Added) repository.Add(BusinessFormFolderMembership.Create(id, FormFolderItemKind.Form, added));
                return new FormFolderFormsResponse(request.FormIds!.ToArray());
            }, CommitPolicy.OnSuccess, cancellationToken);
        });

    internal static FormFolderNode[] Nodes(IEnumerable<BusinessFormFolder> folders) =>
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

    private static ValidationError Field(Error error) => FieldErrors.On(error, error == FormFolderErrors.IconInvalid ? "icon" : "name");

    private static Error Placement(Error error) =>
        error == FormFolderErrors.NameTaken ? FieldErrors.On(error, "name")
        : error == FormFolderErrors.TooDeep || error == FormFolderErrors.Cycle ? FieldErrors.On(error, "parentId")
        : error;

    private static FormFolderResponse Response(BusinessFormFolder folder) => new(folder.Id, folder.ParentId, folder.Name, folder.Revision, folder.ShownIcon);

    private static FormFolderResponse Response(FormFolderRow folder) => new(folder.Id, folder.ParentId, folder.Name, folder.Revision, folder.MenuIcon);

    // The writes see the tree the reader shows: two levels, with the icons they show, whatever the previous version left below.
    private async Task<IReadOnlyList<BusinessFormFolder>> TreeAsync(CancellationToken cancellationToken) =>
        BusinessFormFolder.TwoLevels(await repository.ListAsync(cancellationToken));

    // Administering folders is administering forms (CM1 to CM6): whoever only manages catalogs files them in folders that exist.
    private async Task<bool> CanManageAsync(Guid actor, CancellationToken cancellationToken) =>
        await users.FindByIdAsync(actor, cancellationToken) is { IsActive: true } && await HasAsync(actor, P.Manage, cancellationToken);

    private Task<bool> HasAsync(Guid actor, string permission, CancellationToken cancellationToken) =>
        permissions.HasPermissionAsync(actor, permission, cancellationToken);
}
