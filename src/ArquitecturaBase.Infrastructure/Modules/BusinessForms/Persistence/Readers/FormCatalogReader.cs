using System.Linq.Expressions;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal sealed class FormCatalogReader(ApplicationDbContext dbContext) : IFormCatalogReader
{
    private sealed record CatalogUsageCount(Guid CatalogId, int? UsageCount);
    private sealed class CatalogWithUsage
    {
        public BusinessFormCatalog Catalog { get; init; } = null!;
        public int UsageCount { get; init; }
    }

    private IQueryable<CatalogUsageCount> FindUsageCounts() => dbContext.Database.SqlQueryRaw<CatalogUsageCount>("""
        SELECT (field.value #>> ARRAY['source', 'catalog'])::uuid AS "CatalogId",
               COUNT(DISTINCT definition."Id")::integer AS "UsageCount"
        FROM "FormDefinitions" AS definition
        CROSS JOIN LATERAL jsonb_array_elements(definition."SchemaJson" -> 'fields') AS field(value)
        WHERE field.value #>> ARRAY['source', 'catalog'] IS NOT NULL
          AND NOT definition."IsDeleted"
        GROUP BY 1
        """);

    public async Task<FormCatalogsPageResponse> FindPageAsync(ListFormCatalogsRequest request, bool canEdit, bool canReadForms,
        CancellationToken cancellationToken)
    {
        var nameColumn = request.DisplayCulture == "en" ? "DisplayNameEn" : "DisplayNameEs";
        var descriptionColumn = request.DisplayCulture == "en" ? "DisplayDescriptionEn" : "DisplayDescriptionEs";
        var catalogs = FilterCatalogs(request, nameColumn, descriptionColumn);
        IQueryable<CatalogWithUsage> query;
        if (canReadForms)
        {
            var usages = FindUsageCounts();
            query = from catalog in catalogs
                    join usage in usages on catalog.Id equals usage.CatalogId into usageRows
                    from usage in usageRows.DefaultIfEmpty()
                    select new CatalogWithUsage { Catalog = catalog, UsageCount = usage.UsageCount ?? 0 };
        }
        else
        {
            query = catalogs.Select(catalog => new CatalogWithUsage { Catalog = catalog, UsageCount = 0 });
        }
        var sortable = new Dictionary<string, Expression<Func<CatalogWithUsage, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = value => EF.Property<string>(value.Catalog, nameColumn),
            ["kind"] = value => value.Catalog.Kind,
            ["revision"] = value => value.Catalog.Revision,
            ["optionsCount"] = value => dbContext.Set<BusinessFormCatalogItem>().Count(item => item.CatalogId == value.Catalog.Id && item.IsActive),
            ["usageCount"] = value => value.UsageCount,
        };
        var originGroups = await FilterCatalogs(request with { Kind = null }, nameColumn, descriptionColumn)
            .GroupBy(value => value.Kind)
            .Select(group => new { Kind = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var originCounts = new FormCatalogOriginCounts(originGroups.Sum(group => group.Count),
            originGroups.SingleOrDefault(group => group.Kind == BusinessFormCatalogKind.Manual)?.Count ?? 0,
            originGroups.SingleOrDefault(group => group.Kind == BusinessFormCatalogKind.Filters)?.Count ?? 0);
        var page = await query.ApplySort(SortDescriptor.Parse(request.Sort), sortable, new("name", false), value => value.Catalog.Id)
            .Select(value => new FormCatalogListItemResponse(value.Catalog.Id, EF.Property<string>(value.Catalog, nameColumn),
                EF.Property<string>(value.Catalog, descriptionColumn), value.Catalog.Kind.ToString(), value.Catalog.Kind == BusinessFormCatalogKind.Manual
                    ? dbContext.Set<BusinessFormCatalogItem>().Count(item => item.CatalogId == value.Catalog.Id && item.IsActive) : (int?)null,
                canReadForms ? value.UsageCount : null, value.Catalog.IsActive ? "Active" : "Inactive", null, canEdit))
            .ToPagedResultAsync(request, cancellationToken);

        return new FormCatalogsPageResponse(page.Items, page.Page, page.PageSize, page.TotalCount,
            new FormCatalogFilterCounts(originCounts));
    }

    private IQueryable<BusinessFormCatalog> FilterCatalogs(ListFormCatalogsRequest request, string nameColumn, string descriptionColumn)
    {
        var query = dbContext.Set<BusinessFormCatalog>().AsNoTracking();
        if (request.Kind is not null) query = query.Where(value => value.Kind.ToString() == request.Kind);
        query = request.State switch
        {
            "Active" => query.Where(value => value.IsActive),
            "Inactive" => query.Where(value => !value.IsActive),
            "Unavailable" => query.Where(_ => false),
            _ => query,
        };
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = LikePatterns.Contains(request.Search.Trim());
            query = query.Where(value => EF.Functions.ILike(EF.Property<string>(value, nameColumn), pattern, LikePatterns.EscapeCharacter)
                || EF.Functions.ILike(EF.Property<string>(value, descriptionColumn), pattern, LikePatterns.EscapeCharacter)
                || EF.Functions.ILike(value.Key, pattern, LikePatterns.EscapeCharacter));
        }
        return query;
    }

    public async Task<IReadOnlyList<FormCatalogStoredItemRow>> ListAllItemsAsync(Guid catalogId, CancellationToken cancellationToken) =>
        await dbContext.Set<BusinessFormCatalogItem>().AsNoTracking().Where(item => item.CatalogId == catalogId)
            .OrderBy(item => item.Position).ThenBy(item => item.Id)
            .Select(item => new FormCatalogStoredItemRow(item.Id, item.CatalogId, item.Code, item.TextEs, item.TextEn,
                item.DetailEs, item.DetailEn, item.Position, item.IsActive)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FormCatalogStoredItemRow>> ListOfferItemsAsync(Guid catalogId, CancellationToken cancellationToken) =>
        await dbContext.Set<BusinessFormCatalogItem>().AsNoTracking().Where(item => item.CatalogId == catalogId && item.IsActive)
            .OrderBy(item => item.Position).ThenBy(item => item.Id).Take(201)
            .Select(item => new FormCatalogStoredItemRow(item.Id, item.CatalogId, item.Code, item.TextEs, item.TextEn,
                item.DetailEs, item.DetailEn, item.Position, item.IsActive)).ToListAsync(cancellationToken);

    public Task<FormCatalogStoredRow?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Set<BusinessFormCatalog>().AsNoTracking().Where(value => value.Id == id)
            .Select(value => new FormCatalogStoredRow(value.Id, value.Key, value.NameJson, value.DescriptionJson, value.Kind.ToString(),
                value.Revision, value.IsActive, value.CreatedAtUtc, value.CreatedBy, value.ModifiedAtUtc, value.ModifiedBy,
                dbContext.Set<BusinessFormCatalogItem>().Count(item => item.CatalogId == value.Id), value.FiltersJson, value.ProposalEvidenceJson, value.ProposalState, value.ProposalDecidedByUserId, value.ProposalDecidedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<int> CountUsageAsync(Guid catalogId, CancellationToken cancellationToken) =>
        await FindUsageCounts().Where(value => value.CatalogId == catalogId)
            .Select(value => value.UsageCount)
            .SingleOrDefaultAsync(cancellationToken) ?? 0;

    public async Task<PagedResult<FormCatalogItemResponse>> ListItemsAsync(Guid id, ListFormCatalogItemsRequest request, CancellationToken cancellationToken)
    {
        var query = dbContext.Set<BusinessFormCatalogItem>().AsNoTracking().Where(value => value.CatalogId == id);
        if (request.IsActive.HasValue) query = query.Where(value => value.IsActive == request.IsActive.Value);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = LikePatterns.Contains(request.Search.Trim());
            query = query.Where(value => EF.Functions.ILike(value.Code, pattern, LikePatterns.EscapeCharacter)
                || EF.Functions.ILike(request.DisplayCulture == "en"
                    ? value.TextEn == null || value.TextEn.Trim() == "" ? value.TextEs : value.TextEn
                    : value.TextEs, pattern, LikePatterns.EscapeCharacter));
        }
        var sortable = new Dictionary<string, Expression<Func<BusinessFormCatalogItem, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["position"] = value => value.Position,
            ["code"] = value => value.Code,
            ["text"] = value => request.DisplayCulture == "en"
                ? value.TextEn == null || value.TextEn.Trim() == "" ? value.TextEs : value.TextEn
                : value.TextEs,
            ["isActive"] = value => value.IsActive,
        };
        var rows = await query.ApplySort(SortDescriptor.Parse(request.Sort), sortable, new("position", false), value => value.Id)
            .Select(value => new FormCatalogStoredItemRow(value.Id, value.CatalogId, value.Code, value.TextEs, value.TextEn,
                value.DetailEs, value.DetailEn, value.Position, value.IsActive)).ToPagedResultAsync(request, cancellationToken);
        var items = rows.Items.Select(value => new FormCatalogItemResponse(value.Id, value.Code,
            new Dictionary<string, string?> { ["es"] = value.TextEs, ["en"] = value.TextEn },
            new Dictionary<string, string?> { ["es"] = value.DetailEs, ["en"] = value.DetailEn }, value.Position, value.IsActive)).ToArray();
        return new(items, rows.Page, rows.PageSize, rows.TotalCount);
    }
}
