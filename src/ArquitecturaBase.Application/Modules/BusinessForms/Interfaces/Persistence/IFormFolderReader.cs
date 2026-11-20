using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

/// <summary>Reads the folder tree, what each folder holds and the forms of the menu, without tracking.</summary>
public interface IFormFolderReader
{
    /// <summary>
    /// The tree in two levels: a menu always with its icon, a subfolder without one and nothing below the second level, also over
    /// what the previous version may still write until the second deployment of FoldersAsMenus.
    /// </summary>
    Task<IReadOnlyList<FormFolderRow>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The folders that directly hold something the person sees in that listing: in «Formularios», the published forms of the
    /// overview (with only Decide, those with something for them to decide); in «Diseñar formularios», every form not deleted; in
    /// «Catálogos», every catalog.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListFolderIdsWithItemsAsync(FormFolderListing listing, Guid actorUserId, bool canReadRecords, bool canDecide,
        bool canCreate, CancellationToken cancellationToken);

    /// <summary>The folders of a form or a catalog.</summary>
    Task<IReadOnlyList<Guid>> ListItemFolderIdsAsync(FormFolderItemKind kind, Guid itemId, CancellationToken cancellationToken);

    /// <summary>The forms (not deleted) directly in that folder, not in its subfolders: drafts and deactivated ones too.</summary>
    Task<IReadOnlyList<Guid>> ListFolderFormIdsAsync(Guid folderId, CancellationToken cancellationToken);

    /// <summary>For each of these forms that exists and is not deleted, how many folders it is in; the rest do not come.</summary>
    Task<IReadOnlyList<FormFolderCountRow>> ListFormFolderCountsAsync(IReadOnlyCollection<Guid> formIds, CancellationToken cancellationToken);

    /// <summary>Whether the form (not deleted) or the catalog exists.</summary>
    Task<bool> ExistsItemAsync(FormFolderItemKind kind, Guid itemId, CancellationToken cancellationToken);

    /// <summary>
    /// What deleting the folder takes by cascade: every folder below it, also below the second level, which <see cref="ListAsync"/>
    /// does not read, and the different forms (not deleted) and catalogs they and the folder hold.
    /// </summary>
    Task<FormFolderDeletionCounts> CountDeletionAsync(Guid folderId, CancellationToken cancellationToken);

    /// <summary>
    /// The active forms (published, neither deactivated nor deleted) directly in these folders that this person can open, with their
    /// published name: the same ones the tree of the forms listing shows them.
    /// </summary>
    Task<IReadOnlyList<FormFolderMenuFormRow>> ListMenuFormsAsync(IReadOnlyCollection<Guid> folderIds, string culture,
        Guid actorUserId, bool canReadRecords, bool canCreate, CancellationToken cancellationToken);

    /// <summary>Every active form directly in these folders, with its published name: what whoever administers forms sees in the menu.</summary>
    Task<IReadOnlyList<FormFolderMenuFormRow>> ListActiveMenuFormsAsync(IReadOnlyCollection<Guid> folderIds, string culture,
        CancellationToken cancellationToken);

    /// <summary>
    /// For each menu (a folder of the first level) with catalogs, how many different catalogs (not deleted) its folder and its subfolders
    /// hold, in one read.
    /// </summary>
    Task<IReadOnlyList<FormMenuCatalogCountRow>> ListMenuCatalogCountsAsync(CancellationToken cancellationToken);
}
