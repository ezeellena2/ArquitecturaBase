using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal sealed class FormFolderReader(ApplicationDbContext dbContext) : IFormFolderReader
{
    public async Task<IReadOnlyList<FormFolderRow>> ListAsync(CancellationToken cancellationToken) =>
        (await dbContext.Set<BusinessFormFolder>().AsNoTracking().ToListAsync(cancellationToken))
            .Select(folder => new FormFolderRow(folder.Id, folder.ParentId, folder.Name, folder.NormalizedName, folder.SystemKey,
                folder.MenuPlacement.ToString(), folder.MenuIcon, folder.Revision)).ToArray();

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

    public Task<bool> ExistsItemAsync(FormFolderItemKind kind, Guid itemId, CancellationToken cancellationToken) =>
        (kind == FormFolderItemKind.Form ? Definitions() : Catalogs()).AnyAsync(id => id == itemId, cancellationToken);

    public async Task<FormFolderItemCounts> CountItemsAsync(IReadOnlyCollection<Guid> folderIds, CancellationToken cancellationToken)
    {
        var ids = folderIds.ToArray();
        var forms = await Holding(FormFolderItemKind.Form, Definitions()).Where(membership => ids.Contains(membership.FolderId))
            .Select(membership => membership.ItemId).Distinct().CountAsync(cancellationToken);
        var catalogs = await Holding(FormFolderItemKind.Catalog, Catalogs()).Where(membership => ids.Contains(membership.FolderId))
            .Select(membership => membership.ItemId).Distinct().CountAsync(cancellationToken);
        return new(forms, catalogs);
    }

    public async Task<IReadOnlyList<FormFolderMenuFormRow>> ListMenuFormsAsync(IReadOnlyCollection<Guid> folderIds, string culture,
        CancellationToken cancellationToken)
    {
        var ids = folderIds.ToArray();
        var nameColumn = culture == "en" ? "DisplayNameEn" : "DisplayNameEs";
        return await (from membership in dbContext.Set<BusinessFormFolderMembership>().AsNoTracking()
                      where membership.ItemKind == FormFolderItemKind.Form && ids.Contains(membership.FolderId)
                      join definition in FormOverviewDefinitionQuery.Published(dbContext).Where(definition => definition.DeactivatedAtUtc == null)
                          on membership.ItemId equals definition.Id
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
