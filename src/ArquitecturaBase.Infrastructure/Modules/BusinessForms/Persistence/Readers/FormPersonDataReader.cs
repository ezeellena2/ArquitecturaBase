using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal sealed partial class FormPersonDataReader(ApplicationDbContext dbContext) : IFormPersonDataReader
{
    private sealed class PersonRow
    {
        public Guid UserId { get; init; }
        public string? DisplayName { get; init; }
        public string ValuesJson { get; init; } = "{}";
    }

    public async Task<IReadOnlyList<FormPersonAttributeStoredRow>> ListAttributesAsync(CancellationToken cancellationToken) =>
        await (from attribute in dbContext.Set<BusinessFormPersonAttribute>().AsNoTracking()
               join catalog in dbContext.Set<BusinessFormCatalog>().AsNoTracking() on attribute.SourceCatalogId equals catalog.Id into catalogs
               from catalog in catalogs.DefaultIfEmpty()
               orderby attribute.Position, attribute.Id
               select new FormPersonAttributeStoredRow(attribute.Id, attribute.Key, attribute.NameJson, attribute.OriginalCulture, attribute.Type.ToString(),
                   attribute.SourceCatalogId, attribute.Position, attribute.IsActive, attribute.Revision, catalog == null ? null : catalog.NameJson))
            .ToListAsync(cancellationToken);

    public async Task<FormPersonRecipientDataRow> FindRecipientDataAsync(Guid userId, CancellationToken cancellationToken)
    {
        var attributes = await ListAttributesAsync(cancellationToken);
        var data = await dbContext.Set<BusinessFormPersonData>().AsNoTracking().Where(value => value.UserId == userId)
            .Select(value => new { value.Revision, value.ValuesJson }).SingleOrDefaultAsync(cancellationToken);
        var values = data?.ValuesJson ?? "{}";
        var optionTexts = await FindOptionTextsAsync(attributes, [values], CurrentCulture, cancellationToken);
        return new(userId, data?.Revision, values, attributes, optionTexts);
    }

    public Task<int> CountPeopleWithValueAsync(Guid attributeId, CancellationToken cancellationToken)
    {
        var key = attributeId.ToString("D");
        return dbContext.Set<BusinessFormPersonData>().AsNoTracking()
            .Where(value => EF.Functions.JsonExists(value.ValuesJson, key) && dbContext.Users.Any(user => user.Id == value.UserId))
            .CountAsync(cancellationToken);
    }

    public async Task<PagedResult<FormPersonDataTableRow>> ListPeopleAsync(ListFormPersonDataRequest request, CancellationToken cancellationToken)
    {
        var page = await FindPeople(request).ToPagedResultAsync(request, cancellationToken);
        var attributes = await ListAttributesAsync(cancellationToken);
        var optionTexts = await FindOptionTextsAsync(attributes, page.Items.Select(value => value.ValuesJson), request.DisplayCulture, cancellationToken);
        return new(page.Items.Select(value => new FormPersonDataTableRow(value.UserId, value.DisplayName, value.ValuesJson, optionTexts)).ToArray(),
            page.Page, page.PageSize, page.TotalCount);
    }

    private IQueryable<PersonRow> FindPeople(ListFormPersonDataRequest request)
    {
        var query = from user in dbContext.Users.AsNoTracking()
                    join data in dbContext.Set<BusinessFormPersonData>().AsNoTracking() on user.Id equals data.UserId into values
                    from data in values.DefaultIfEmpty()
                    select new PersonRow { UserId = user.Id, DisplayName = user.DisplayName, ValuesJson = data == null ? "{}" : data.ValuesJson };
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = LikePatterns.Contains(request.Search.Trim());
            query = query.Where(value => value.DisplayName != null && EF.Functions.ILike(value.DisplayName, pattern, LikePatterns.EscapeCharacter));
        }
        var sortable = new Dictionary<string, Expression<Func<PersonRow, object?>>>(StringComparer.OrdinalIgnoreCase)
        { ["displayName"] = value => value.DisplayName };
        return query.ApplySort(SortDescriptor.Parse(request.Sort), sortable, new("displayName", false), value => value.UserId);
    }

    public async Task<PagedResult<FormPersonAttributeOptionRow>> ListOptionsAsync(Guid sourceCatalogId, ListFormPersonAttributeOptionsRequest request,
        CancellationToken cancellationToken) => await FindOptions(sourceCatalogId, request).ToPagedResultAsync(request, cancellationToken);

    private IQueryable<FormPersonAttributeOptionRow> FindOptions(Guid sourceCatalogId, ListFormPersonAttributeOptionsRequest request)
    {
        var items = from item in dbContext.Set<BusinessFormCatalogItem>().AsNoTracking()
                    join catalog in dbContext.Set<BusinessFormCatalog>().AsNoTracking() on item.CatalogId equals catalog.Id
                    where catalog.Id == sourceCatalogId && catalog.Kind == BusinessFormCatalogKind.Manual && catalog.IsActive && item.IsActive
                    select item;
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = LikePatterns.Contains(request.Search.Trim());
            items = items.Where(value => EF.Functions.ILike(request.DisplayCulture == "en"
                ? value.TextEn == null || value.TextEn.Trim() == "" ? value.TextEs! : value.TextEn
                : value.TextEs == null || value.TextEs.Trim() == "" ? value.TextEn! : value.TextEs, pattern, LikePatterns.EscapeCharacter));
        }
        var sortable = new Dictionary<string, Expression<Func<BusinessFormCatalogItem, object?>>>(StringComparer.OrdinalIgnoreCase)
        { ["text"] = value => request.DisplayCulture == "en" ? value.TextEn == null || value.TextEn.Trim() == "" ? value.TextEs : value.TextEn
            : value.TextEs == null || value.TextEs.Trim() == "" ? value.TextEn : value.TextEs };
        return items.ApplySort(SortDescriptor.Parse(request.Sort), sortable, new("text", false), value => value.Id)
            .Select(value => new FormPersonAttributeOptionRow(value.Id, request.DisplayCulture == "en"
                ? value.TextEn == null || value.TextEn.Trim() == "" ? value.TextEs! : value.TextEn
                : value.TextEs == null || value.TextEs.Trim() == "" ? value.TextEn! : value.TextEs));
    }

    private async Task<Dictionary<Guid, FormPersonOptionTextRow>> FindOptionTextsAsync(IReadOnlyList<FormPersonAttributeStoredRow> attributes,
        IEnumerable<string> rows, string culture, CancellationToken cancellationToken)
    {
        var sources = attributes.Where(value => value.Type == "Catalog").ToDictionary(value => value.Id.ToString("D"), value => value.SourceCatalogId, StringComparer.Ordinal);
        var requested = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var json in rows)
        {
            using var document = JsonDocument.Parse(json);
            foreach (var property in document.RootElement.EnumerateObject())
                if (sources.TryGetValue(property.Name, out var source) && source.HasValue && property.Value.ValueKind == JsonValueKind.String
                    && Guid.TryParseExact(property.Value.GetString(), "D", out var itemId))
                {
                    if (!requested.TryGetValue(itemId, out var catalogs)) requested.Add(itemId, catalogs = []);
                    catalogs.Add(source.Value);
                }
        }
        if (requested.Count == 0) return [];
        var ids = requested.Keys.ToArray();
        var items = await dbContext.Set<BusinessFormCatalogItem>().AsNoTracking().Where(value => ids.Contains(value.Id))
            .Select(value => new { value.Id, value.CatalogId, Text = culture == "en"
                ? value.TextEn == null || value.TextEn.Trim() == "" ? value.TextEs! : value.TextEn
                : value.TextEs == null || value.TextEs.Trim() == "" ? value.TextEn! : value.TextEs }).ToListAsync(cancellationToken);
        return items.Where(value => requested[value.Id].Contains(value.CatalogId))
            .ToDictionary(value => value.Id, value => new FormPersonOptionTextRow(value.CatalogId, value.Text));
    }

    private static string CurrentCulture => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es";
}
