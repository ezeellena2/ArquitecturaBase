using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// Files what the system creates (the catalogs of ARCA, a standard of the library) in the folder it keeps under a key. The folder is
/// found by its key even after a rename or a move; if someone deleted it, it is created again at the top, outside the menu, like any
/// new folder. A top folder with that same name and no key is adopted instead of repeating the name. It runs inside its caller's
/// transaction, takes the folders lock after the caller's own locks and never saves; it has no route of its own.
/// </summary>
internal sealed class FormSystemFolderLinker(IFormFolderRepository repository)
{
    /// <returns>The folder the item is in.</returns>
    public async Task<Result<Guid>> FileAsync(FormFolderItemKind kind, Guid itemId, string systemKey, string name, CancellationToken cancellationToken)
    {
        await repository.LockAsync(cancellationToken);
        var folders = await repository.ListAsync(cancellationToken);
        var folder = folders.SingleOrDefault(value => value.SystemKey == systemKey);
        if (folder is null)
        {
            var created = BusinessFormFolder.Create(null, name, systemKey);
            if (created.IsFailure) return created.Error;
            var sameName = folders.SingleOrDefault(value => value.ParentId is null && value.NormalizedName == created.Value.NormalizedName);
            if (sameName is { SystemKey: not null }) return FormFolderErrors.NameTaken;
            if (sameName is not null) sameName.AssignSystemKey(systemKey);
            else repository.Add(created.Value);
            folder = sameName ?? created.Value;
        }
        if ((await repository.ListMembershipsAsync(kind, itemId, cancellationToken)).All(value => value.FolderId != folder.Id))
            repository.Add(BusinessFormFolderMembership.Create(folder.Id, kind, itemId));
        return folder.Id;
    }
}
