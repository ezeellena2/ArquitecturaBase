using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

/// <summary>
/// The folders of forms and catalogs, for writing under the folders lock. Every write reads the whole tree, also what this same
/// transaction already added, so two operations never build a cycle or two equal names between them.
/// </summary>
public interface IFormFolderRepository
{
    /// <summary>Puts every folder operation in line. Taken after the other forms locks.</summary>
    Task LockAsync(CancellationToken cancellationToken);

    /// <summary>Every folder, tracked, including the ones this transaction added.</summary>
    Task<IReadOnlyList<BusinessFormFolder>> ListAsync(CancellationToken cancellationToken);

    /// <summary>The folders of an item, tracked, including the ones this transaction added.</summary>
    Task<IReadOnlyList<BusinessFormFolderMembership>> ListMembershipsAsync(FormFolderItemKind kind, Guid itemId, CancellationToken cancellationToken);

    void Add(BusinessFormFolder folder);

    void Add(BusinessFormFolderMembership membership);

    /// <summary>Deletes the folder; its subfolders and memberships go with it.</summary>
    void Remove(BusinessFormFolder folder);

    void Remove(BusinessFormFolderMembership membership);
}
