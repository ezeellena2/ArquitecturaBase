using System.Globalization;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// «Ordenar por…» of a Manual catalog: every option by its text, its code or the value of one field. Options that tie keep the
/// current order (Position, then Id), so sorting by a field groups without undoing the order inside, and options without a
/// value go last in both directions. Texts compare with the rules of the catalog's original language, reading the numbers
/// inside them by value (117 before 10030); a field of another catalog sorts by the text of its option.
/// </summary>
internal static class FormCatalogItemSortPolicy
{
    public static IReadOnlyList<BusinessFormCatalogItem> Order(BusinessFormCatalog catalog, IEnumerable<BusinessFormCatalogItem> items,
        FormCatalogItemSortRequest sort, BusinessFormCatalogField? field, IReadOnlyDictionary<Guid, IReadOnlyList<FormCatalogStoredItemRow>> sources)
    {
        var texts = StringComparer.Create(CultureInfo.GetCultureInfo(catalog.OriginalCulture == "en" ? "en-US" : "es-AR"), CompareOptions.NumericOrdering);
        if (field is null)
            return By(items, item => Present(sort.Column == "code" ? item.Code : item.TextFor(catalog.OriginalCulture)), texts, sort.Descending);
        string? Value(BusinessFormCatalogItem item) =>
            FormCatalogFieldPolicy.ReadValues(item.FieldValuesJson).TryGetValue(field.Id.ToString("D"), out var raw) ? FormCatalogFieldPolicy.Input(raw) : null;
        switch (field.Type)
        {
            case "integer" or "decimal":
                return By(items, item => decimal.TryParse(Value(item), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture,
                    out var number) ? (true, number) : (false, 0m), Comparer<decimal>.Default, sort.Descending);
            case "date":
                return By(items, item => DateOnly.TryParseExact(Value(item), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    ? (true, date) : (false, default), Comparer<DateOnly>.Default, sort.Descending);
            case "boolean":
                return By(items, item => Value(item) switch { "true" => (true, true), "false" => (true, false), _ => (false, false) },
                    Comparer<bool>.Default, sort.Descending);
            case "catalog":
                var options = sources.GetValueOrDefault(field.SourceCatalogId ?? Guid.Empty)?.ToDictionary(option => option.Id) ?? [];
                return By(items, item => Guid.TryParse(Value(item), out var optionId) && options.TryGetValue(optionId, out var option)
                    ? Present(option.TextFor(catalog.OriginalCulture)) : (false, ""), texts, sort.Descending);
            default:
                return By(items, item => Present(Value(item)), texts, sort.Descending);
        }
    }

    private static (bool Present, string Value) Present(string? text) => string.IsNullOrWhiteSpace(text) ? (false, "") : (true, text);

    private static BusinessFormCatalogItem[] By<T>(IEnumerable<BusinessFormCatalogItem> items, Func<BusinessFormCatalogItem, (bool Present, T Value)> key,
        IComparer<T> comparer, bool descending)
    {
        var direction = descending ? Comparer<T>.Create((left, right) => comparer.Compare(right, left)) : comparer;
        return items.Select(item => (Item: item, Key: key(item)))
            .OrderBy(pair => pair.Key.Present ? 0 : 1).ThenBy(pair => pair.Key.Value, direction)
            .ThenBy(pair => pair.Item.Position).ThenBy(pair => pair.Item.Id)
            .Select(pair => pair.Item).ToArray();
    }
}
