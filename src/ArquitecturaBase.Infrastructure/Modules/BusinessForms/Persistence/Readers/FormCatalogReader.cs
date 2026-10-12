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
    public Task<PagedResult<FormCatalogListItemResponse>> ListAsync(ListFormCatalogsRequest request, bool canEdit, bool canReadForms,
        CancellationToken cancellationToken)
    {
        var nameColumn = request.DisplayCulture == "en" ? "DisplayNameEn" : "DisplayNameEs";
        var descriptionColumn = request.DisplayCulture == "en" ? "DisplayDescriptionEn" : "DisplayDescriptionEs";
        // K6 introduces references; until then a readable catalog has no usage rows to count.
        int? usageCount = canReadForms ? 0 : null;
        var sortable = new Dictionary<string, Expression<Func<BusinessFormCatalog, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = value => EF.Property<string>(value, nameColumn),
            ["kind"] = value => value.Kind,
            ["revision"] = value => value.Revision,
            ["optionsCount"] = value => dbContext.Set<BusinessFormCatalogItem>().Count(item => item.CatalogId == value.Id && item.IsActive),
            ["usageCount"] = _ => 0,
        };
        var query = dbContext.Set<BusinessFormCatalog>().AsNoTracking();
        if (request.Kind is not null) query = query.Where(value => value.Kind.ToString() == request.Kind);
        if (request.IsActive.HasValue) query = query.Where(value => value.IsActive == request.IsActive.Value);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = LikePatterns.Contains(request.Search.Trim());
            query = query.Where(value => EF.Functions.ILike(EF.Property<string>(value, nameColumn), pattern, LikePatterns.EscapeCharacter)
                || EF.Functions.ILike(EF.Property<string>(value, descriptionColumn), pattern, LikePatterns.EscapeCharacter)
                || EF.Functions.ILike(value.Key, pattern, LikePatterns.EscapeCharacter));
        }

        return query.ApplySort(SortDescriptor.Parse(request.Sort), sortable, new("name", false), value => value.Id)
            .Select(value => new FormCatalogListItemResponse(value.Id, EF.Property<string>(value, nameColumn),
                EF.Property<string>(value, descriptionColumn), value.Kind.ToString(), value.Kind == BusinessFormCatalogKind.Manual
                    ? dbContext.Set<BusinessFormCatalogItem>().Count(item => item.CatalogId == value.Id && item.IsActive) : (int?)null,
                usageCount, value.IsActive ? "Active" : "Inactive", null, canEdit && value.Kind == BusinessFormCatalogKind.Manual))
            .ToPagedResultAsync(request, cancellationToken);
    }

    public Task<FormCatalogStoredRow?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Set<BusinessFormCatalog>().AsNoTracking().Where(value => value.Id == id)
            .Select(value => new FormCatalogStoredRow(value.Id, value.Key, value.NameJson, value.DescriptionJson, value.Kind.ToString(),
                value.Revision, value.IsActive, value.CreatedAtUtc, value.CreatedBy, value.ModifiedAtUtc, value.ModifiedBy,
                dbContext.Set<BusinessFormCatalogItem>().Count(item => item.CatalogId == value.Id)))
            .SingleOrDefaultAsync(cancellationToken);

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
