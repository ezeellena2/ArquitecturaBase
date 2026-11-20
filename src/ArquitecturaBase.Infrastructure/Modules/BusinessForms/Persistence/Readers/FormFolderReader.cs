using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal sealed class FormFolderReader(ApplicationDbContext dbContext) : IFormFolderReader
{
    // Two levels, also over what the previous version may still write until the next migration folds it again (FoldersAsMenus), as the
    // writes see them (BusinessFormFolder.TwoLevels): a menu without an icon reads as the folder, a subfolder with one reads without it,
    // and nothing below the second level is read, neither the folder nor, since the tree and the menu start here, what it holds.
    public async Task<IReadOnlyList<FormFolderRow>> ListAsync(CancellationToken cancellationToken) =>
        BusinessFormFolder.TwoLevels(await dbContext.Set<BusinessFormFolder>().AsNoTracking().ToListAsync(cancellationToken))
            .Select(folder => new FormFolderRow(folder.Id, folder.ParentId, folder.Name, folder.NormalizedName, folder.SystemKey,
                folder.ShownIcon, folder.Revision)).ToArray();

    public async Task<IReadOnlyList<Guid>> ListFolderIdsWithItemsAsync(FormFolderListing listing, Guid actorUserId, bool canReadRecords,
        bool canDecide, bool canCreate, CancellationToken cancellationToken)
    {
        var memberships = listing switch
        {
            FormFolderListing.Forms => Holding(FormFolderItemKind.Form, FormOverviewDefinitionQuery.VisibleIds(dbContext, actorUserId, canReadRecords, canCreate)),
            FormFolderListing.Designs => Holding(FormFolderItemKind.Form, Definitions()),
            _ => Holding(FormFolderItemKind.Catalog, Catalogs()),
        };
        return await memberships.Select(membership => membership.FolderId).Distinct().ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListItemFolderIdsAsync(FormFolderItemKind kind, Guid itemId, CancellationToken cancellationToken) =>
        await dbContext.Set<BusinessFormFolderMembership>().AsNoTracking()
            .Where(membership => membership.ItemKind == kind && membership.ItemId == itemId)
            .OrderBy(membership => membership.FolderId).Select(membership => membership.FolderId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListFolderFormIdsAsync(Guid folderId, CancellationToken cancellationToken) =>
        await Holding(FormFolderItemKind.Form, Definitions()).Where(membership => membership.FolderId == folderId)
            .OrderBy(membership => membership.ItemId).Select(membership => membership.ItemId).ToListAsync(cancellationToken);

    // Every folder counts, also one the tree does not read below the second level: the limit is the one «Carpetas…» saves.
    public async Task<IReadOnlyList<FormFolderCountRow>> ListFormFolderCountsAsync(IReadOnlyCollection<Guid> formIds, CancellationToken cancellationToken)
    {
        var ids = formIds.ToArray();
        var memberships = dbContext.Set<BusinessFormFolderMembership>().AsNoTracking().Where(membership => membership.ItemKind == FormFolderItemKind.Form);
        return await dbContext.Set<BusinessFormDefinition>().AsNoTracking().Where(definition => ids.Contains(definition.Id))
            .Select(definition => new FormFolderCountRow(definition.Id, memberships.Count(membership => membership.ItemId == definition.Id)))
            .ToListAsync(cancellationToken);
    }

    public Task<bool> ExistsItemAsync(FormFolderItemKind kind, Guid itemId, CancellationToken cancellationToken) =>
        (kind == FormFolderItemKind.Form ? Definitions() : Catalogs()).AnyAsync(id => id == itemId, cancellationToken);

    // The whole subtree the delete takes by cascade, also below the second level.
    public async Task<FormFolderDeletionCounts> CountDeletionAsync(Guid folderId, CancellationToken cancellationToken)
    {
        var nodes = await dbContext.Set<BusinessFormFolder>().AsNoTracking()
            .Select(folder => new { folder.Id, folder.ParentId, folder.NormalizedName }).ToListAsync(cancellationToken);
        var ids = FormFolderTree.Subtree(folderId, nodes.Select(node => new FormFolderNode(node.Id, node.ParentId, node.NormalizedName)).ToArray()).ToArray();
        var forms = await Holding(FormFolderItemKind.Form, Definitions()).Where(membership => ids.Contains(membership.FolderId))
            .Select(membership => membership.ItemId).Distinct().CountAsync(cancellationToken);
        var catalogs = await Holding(FormFolderItemKind.Catalog, Catalogs()).Where(membership => ids.Contains(membership.FolderId))
            .Select(membership => membership.ItemId).Distinct().CountAsync(cancellationToken);
        return new(ids.Length - 1, forms, catalogs);
    }

    // The same forms the tree shows this person, not deactivated ones.
    public Task<IReadOnlyList<FormFolderMenuFormRow>> ListMenuFormsAsync(IReadOnlyCollection<Guid> folderIds, string culture,
        Guid actorUserId, bool canReadRecords, bool canCreate, CancellationToken cancellationToken) =>
        MenuFormsAsync(folderIds, culture, FormOverviewDefinitionQuery.VisibleIds(dbContext, actorUserId, canReadRecords, canCreate), cancellationToken);

    public Task<IReadOnlyList<FormFolderMenuFormRow>> ListActiveMenuFormsAsync(IReadOnlyCollection<Guid> folderIds, string culture,
        CancellationToken cancellationToken) => MenuFormsAsync(folderIds, culture, null, cancellationToken);

    // A menu is a folder of the first level (BusinessFormFolder.MaxDepth is two): its own catalogs and its subfolders' count once.
    public async Task<IReadOnlyList<FormMenuCatalogCountRow>> ListMenuCatalogCountsAsync(CancellationToken cancellationToken) =>
        await (from membership in Holding(FormFolderItemKind.Catalog, Catalogs())
               join folder in dbContext.Set<BusinessFormFolder>().AsNoTracking() on membership.FolderId equals folder.Id
               group membership.ItemId by folder.ParentId ?? folder.Id into menu
               select new FormMenuCatalogCountRow(menu.Key, menu.Distinct().Count()))
            .ToListAsync(cancellationToken);

    // The active forms directly in these folders: every one, or only the visible ones when they come.
    private async Task<IReadOnlyList<FormFolderMenuFormRow>> MenuFormsAsync(IReadOnlyCollection<Guid> folderIds, string culture,
        IQueryable<Guid>? visible, CancellationToken cancellationToken)
    {
        var ids = folderIds.ToArray();
        var nameColumn = culture == "en" ? "DisplayNameEn" : "DisplayNameEs";
        var active = FormOverviewDefinitionQuery.Published(dbContext).Where(definition => definition.DeactivatedAtUtc == null);
        if (visible is not null) active = active.Where(definition => visible.Contains(definition.Id));
        return await (from membership in dbContext.Set<BusinessFormFolderMembership>().AsNoTracking()
                      where membership.ItemKind == FormFolderItemKind.Form && ids.Contains(membership.FolderId)
                      join definition in active on membership.ItemId equals definition.Id
                      join version in dbContext.Set<BusinessFormVersion>().AsNoTracking().Where(version => version.PublishedCultures != null)
                          on definition.ActiveVersionId equals version.Id
                      select new FormFolderMenuFormRow(membership.FolderId, definition.Id,
                          version.PublishedCultures!.Contains(culture) ? EF.Property<string>(version, nameColumn)
                          : version.Culture == "en" ? EF.Property<string>(version, "DisplayNameEn") : EF.Property<string>(version, "DisplayNameEs")))
            .ToListAsync(cancellationToken);
    }

    // The memberships of a kind whose item is among the visible ones: a deleted form hides its memberships without losing them.
    private IQueryable<BusinessFormFolderMembership> Holding(FormFolderItemKind kind, IQueryable<Guid> visible) =>
        dbContext.Set<BusinessFormFolderMembership>().AsNoTracking().Where(membership => membership.ItemKind == kind && visible.Contains(membership.ItemId));

    private IQueryable<Guid> Definitions() => dbContext.Set<BusinessFormDefinition>().AsNoTracking().Select(definition => definition.Id);

    private IQueryable<Guid> Catalogs() => dbContext.Set<BusinessFormCatalog>().AsNoTracking().Select(catalog => catalog.Id);
}
