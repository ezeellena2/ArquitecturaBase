using System.Globalization;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

// The options of a Manual catalog as a person reads them in Excel, in cells that reimport without changes.
internal static class FormCatalogExportPolicy
{
    // An Excel number keeps 15 significant digits.
    private const int ExcelDigits = 15;

    // The template's columns, with each field headed by its name instead of its key.
    public static IReadOnlyList<FormCatalogImportTemplateColumn> Columns(BusinessFormCatalogValueType valueType, string culture,
        string originalCulture, IReadOnlyList<BusinessFormCatalogField> fields, bool translation)
    {
        var active = fields.Where(field => field.IsActive).ToArray();
        var byKey = active.ToDictionary(field => field.Id.ToString("D"), StringComparer.Ordinal);
        return FormCatalogImportTemplate.Columns(valueType, culture, originalCulture, fields, translation).Select(column =>
            byKey.TryGetValue(column.Key, out var field) ? column with { Header = NamedHeader(field, active, culture) } : column).ToArray();
    }

    public static IReadOnlyList<IReadOnlyList<FormWorkbookCell>> Rows(IReadOnlyList<FormCatalogImportTemplateColumn> columns,
        IReadOnlyList<FormCatalogStoredItemRow> items, BusinessFormCatalogValueType valueType, string originalCulture,
        IReadOnlyList<BusinessFormCatalogField> fields, IReadOnlyDictionary<Guid, IReadOnlyList<FormCatalogStoredItemRow>> sources, string culture)
    {
        var byKey = fields.ToDictionary(field => field.Id.ToString("D"), StringComparer.Ordinal);
        var indexes = sources.ToDictionary(pair => pair.Key, pair => FormCatalogFieldPolicy.CreateSourceIndex(pair.Value));
        var options = sources.ToDictionary(pair => pair.Key, pair => pair.Value.ToDictionary(option => option.Id));
        return items.Select(item =>
        {
            var values = FormCatalogFieldPolicy.ReadValues(item.FieldValuesJson);
            return (IReadOnlyList<FormWorkbookCell>)columns.Select(column => column.Key switch
            {
                // «Texto» and «Detalle» are the original; the template has a translation column only for the other language.
                "code" => Text(item.Code), "text" => Text(item.TextIn(originalCulture)), "detail" => Text(item.DetailIn(originalCulture)),
                "textEn" => Text(item.TextIn("en")), "textEs" => Text(item.TextIn("es")), "detailEn" => Text(item.DetailIn("en")), "detailEs" => Text(item.DetailIn("es")),
                "active" => Text(YesOrNo(item.IsActive, culture)), "value" => Typed(valueType.ToString().ToLowerInvariant(), item.Value, culture),
                _ => values.TryGetValue(column.Key, out var value) && FormCatalogFieldPolicy.Input(value) is { } input
                    ? Field(byKey[column.Key], input, indexes, options, culture) : Text(null),
            }).ToArray();
        }).ToArray();
    }

    // Its name in the menu's language, or else in the other one. A name that the import would read as a fixed column, as another
    // field or as an old canonical header goes with [campo]/[field]; one still ambiguous, by the template's key header.
    private static string NamedHeader(BusinessFormCatalogField field, IReadOnlyList<BusinessFormCatalogField> fields, string culture)
    {
        var name = FormRecordPresentationPolicy.Text(field.Name, culture);
        string[] headers = [name, FormCatalogImportTemplate.ExtraFieldHeader(name, culture), FormCatalogImportTemplate.FieldHeader(field, culture, false)];
        // A canonical match would read the column as canonical: «Sí» would no longer be a yes.
        return headers.First(header => FormCatalogImportPolicy.MatchHeader(header, fields) is { Canonical: false } match
            && match.Key == field.Id.ToString("D"));
    }

    private static FormWorkbookCell Field(BusinessFormCatalogField field, string value, IReadOnlyDictionary<Guid, FormCatalogFieldSourceIndex> indexes,
        IReadOnlyDictionary<Guid, Dictionary<Guid, FormCatalogStoredItemRow>> options, string culture) => field.Type switch
    {
        "boolean" => Text(YesOrNo(value == "true", culture)),
        "catalog" => Text(SourceLabel(field, value, indexes, options, culture)),
        "integer" or "decimal" or "date" => Typed(field.Type, value, culture),
        _ => Text(value),
    };

    // The option's text in the menu's language. If reimporting it would pick another option (an active one with the same text),
    // its code, and only then its identity, which is also what a value outside the source keeps.
    private static string SourceLabel(BusinessFormCatalogField field, string value, IReadOnlyDictionary<Guid, FormCatalogFieldSourceIndex> indexes,
        IReadOnlyDictionary<Guid, Dictionary<Guid, FormCatalogStoredItemRow>> options, string culture)
    {
        if (field.SourceCatalogId is not { } sourceId || !Guid.TryParse(value, out var id) || !indexes.TryGetValue(sourceId, out var index)
            || !options[sourceId].TryGetValue(id, out var option)) return value;
        var saved = id.ToString("D");
        return new[] { option.TextFor(culture), option.Code }
            .FirstOrDefault(label => index.Resolve(label, id) is { IsSuccess: true } resolved && resolved.Value == saved) ?? saved;
    }

    // Integer, decimal and date go as Excel numbers and dates in the menu's format. Beyond 15 significant digits, or before 1900,
    // Excel cannot hold the value: the cell is its exact canonical text, which reads the same in either menu.
    private static FormWorkbookCell Typed(string type, string? value, string culture)
    {
        if (value is null) return Text(null);
        if (type == "date")
        {
            var date = DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            return date.Year < 1900 ? Text(value) : new("date", date.ToDateTime(TimeOnly.MinValue), culture == "en" ? "mm/dd/yyyy" : "dd/mm/yyyy");
        }
        if (value.TrimStart('-').Replace(".", "", StringComparison.Ordinal).Trim('0').Length > ExcelDigits) return Text(value);
        // As many decimals as the value has: a whole number has no decimal separator.
        var point = value.IndexOf('.', StringComparison.Ordinal);
        var format = point < 0 ? "#,##0" : "#,##0." + new string('0', value.Length - point - 1);
        return type == "integer" ? new("integer", long.Parse(value, CultureInfo.InvariantCulture), format)
            : new("decimal", decimal.Parse(value, CultureInfo.InvariantCulture), format);
    }

    private static string YesOrNo(bool value, string culture) => FormCatalogTexts.Get(value ? "Yes" : "No", culture);

    private static FormWorkbookCell Text(string? value) => new("text", value);
}
