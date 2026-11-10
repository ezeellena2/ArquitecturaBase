using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

/// <summary>Reads the folder tree, what each folder holds and the forms of the menu, without tracking.</summary>
public interface IFormFolderReader
{
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

    /// <summary>Whether the form (not deleted) or the catalog exists.</summary>
    Task<bool> ExistsItemAsync(FormFolderItemKind kind, Guid itemId, CancellationToken cancellationToken);

    /// <summary>How many different forms (not deleted) and catalogs these folders hold.</summary>
    Task<FormFolderItemCounts> CountItemsAsync(IReadOnlyCollection<Guid> folderIds, CancellationToken cancellationToken);

    /// <summary>The active forms (published, neither deactivated nor deleted) directly in these folders, with their published name.</summary>
    Task<IReadOnlyList<FormFolderMenuFormRow>> ListMenuFormsAsync(IReadOnlyCollection<Guid> folderIds, string culture,
        CancellationToken cancellationToken);
}
