using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal sealed class FormCatalogReader(ApplicationDbContext dbContext) : IFormCatalogReader
{
    public Task<FormCatalogStoredRow?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Set<BusinessFormCatalog>().AsNoTracking().Where(value => value.Id == id)
            .Select(value => new FormCatalogStoredRow(value.Id, value.Key, value.NameJson, value.DescriptionJson, value.Kind.ToString(),
                value.Revision, value.IsActive, value.CreatedAtUtc, value.CreatedBy, value.ModifiedAtUtc, value.ModifiedBy,
                dbContext.Set<BusinessFormCatalogItem>().Count(item => item.CatalogId == value.Id)))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<PagedResult<FormCatalogItemResponse>> ListItemsAsync(Guid id, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = dbContext.Set<BusinessFormCatalogItem>().AsNoTracking().Where(value => value.CatalogId == id);
        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query.OrderBy(value => value.Position).ThenBy(value => value.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(value => new FormCatalogStoredItemRow(value.Id, value.CatalogId, value.Code, value.TextEs, value.TextEn,
                value.DetailEs, value.DetailEn, value.Position, value.IsActive)).ToListAsync(cancellationToken);
        var items = rows.Select(value => new FormCatalogItemResponse(value.Id, value.Code,
            new Dictionary<string, string?> { ["es"] = value.TextEs, ["en"] = value.TextEn },
            new Dictionary<string, string?> { ["es"] = value.DetailEs, ["en"] = value.DetailEn }, value.Position, value.IsActive)).ToArray();
        return new(items, page, pageSize, totalCount);
    }
}
