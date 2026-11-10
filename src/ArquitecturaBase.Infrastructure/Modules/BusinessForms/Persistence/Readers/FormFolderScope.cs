using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

/// <summary>
/// The folder filter of the three listings (CP1-A), in SQL before counting, sorting and paging: a folder takes what is in it or in
/// any of its subfolders (a recursive query), each item once; «Sin carpeta» takes what is in no folder.
/// </summary>
internal static class FormFolderScope
{
    public static IQueryable<BusinessFormDefinition> InFolder(this IQueryable<BusinessFormDefinition> definitions, ApplicationDbContext dbContext,
        Guid? folderId, bool withoutFolder)
    {
        if (folderId is { } id)
        {
            var inside = ItemIds(dbContext, id, FormFolderItemKind.Form);
            return definitions.Where(definition => inside.Contains(definition.Id));
        }
        if (!withoutFolder) return definitions;
        var filed = FiledIds(dbContext, FormFolderItemKind.Form);
        return definitions.Where(definition => !filed.Contains(definition.Id));
    }

    public static IQueryable<BusinessFormCatalog> InFolder(this IQueryable<BusinessFormCatalog> catalogs, ApplicationDbContext dbContext,
        Guid? folderId, bool withoutFolder)
    {
        if (folderId is { } id)
        {
            var inside = ItemIds(dbContext, id, FormFolderItemKind.Catalog);
            return catalogs.Where(catalog => inside.Contains(catalog.Id));
        }
        if (!withoutFolder) return catalogs;
        var filed = FiledIds(dbContext, FormFolderItemKind.Catalog);
        return catalogs.Where(catalog => !filed.Contains(catalog.Id));
    }

    // Starts with SELECT so EF can compose it; the recursive part lives in the subquery.
    private static IQueryable<Guid> ItemIds(ApplicationDbContext dbContext, Guid folderId, FormFolderItemKind kind) =>
        dbContext.Database.SqlQueryRaw<Guid>("""
            SELECT DISTINCT membership."ItemId" AS "Value"
            FROM "FormFolderMemberships" AS membership
            WHERE membership."ItemKind" = {1} AND membership."FolderId" IN (
                WITH RECURSIVE subtree AS (
                    SELECT folder."Id" FROM "FormFolders" AS folder WHERE folder."Id" = {0}
                    UNION ALL
                    SELECT child."Id" FROM "FormFolders" AS child JOIN subtree ON child."ParentId" = subtree."Id"
                )
                SELECT subtree."Id" FROM subtree)
            """.ReplaceLineEndings("\n"), folderId, kind.ToString());

    private static IQueryable<Guid> FiledIds(ApplicationDbContext dbContext, FormFolderItemKind kind) =>
        dbContext.Set<BusinessFormFolderMembership>().AsNoTracking().Where(membership => membership.ItemKind == kind).Select(membership => membership.ItemId);
}
