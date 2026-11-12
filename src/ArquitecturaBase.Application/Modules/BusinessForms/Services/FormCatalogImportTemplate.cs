using System.Globalization;
using System.Text;
using ArquitecturaBase.Application.Common.Formatting;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal static class FormCatalogImportTemplate
{
    // «Texto» and «Detalle» are the catalog's original; the other language has its own optional translation column,
    // which `translation: false` leaves out (an absent column keeps the translation on import).
    public static IReadOnlyList<FormCatalogImportTemplateColumn> Columns(BusinessFormCatalogValueType valueType, string culture,
        string originalCulture, IReadOnlyList<BusinessFormCatalogField>? fields = null, bool translation = true)
    {
        var suffix = TranslationSuffix(originalCulture);
        var columns = new List<FormCatalogImportTemplateColumn>
        {
            new("code", FormCatalogTexts.Get("HeaderCode", culture), ""),
            new("text", FormCatalogTexts.Get("HeaderText", culture), FormCatalogTexts.Get("RuleText", culture)),
        };
        if (translation) columns.Add(new("text" + suffix, FormCatalogTexts.Get("HeaderText" + suffix, culture), FormCatalogTexts.Get("RuleTextEn", culture)));
        columns.Add(new("detail", FormCatalogTexts.Get("HeaderDetail", culture), FormCatalogTexts.Get("RuleDetail", culture)));
        if (translation) columns.Add(new("detail" + suffix, FormCatalogTexts.Get("HeaderDetail" + suffix, culture), FormCatalogTexts.Get("RuleDetail", culture)));
        columns.Add(new("active", FormCatalogTexts.Get("HeaderActive", culture), FormCatalogTexts.Get("RuleActive", culture)));
        if (valueType != BusinessFormCatalogValueType.Text)
            columns.Add(new("value", ValueHeader(valueType, culture, false), FormCatalogTexts.Get("RuleValue" + valueType, culture)));
        foreach (var field in (fields ?? []).Where(field => field.IsActive).OrderBy(field => field.Position))
            columns.Add(new(field.Id.ToString("D"), FieldHeader(field, culture, false), FieldRule(field, culture)));
        return columns;
    }

    // The suffix of the translation columns: "En" for a Spanish original, "Es" for an English one.
    public static string TranslationSuffix(string originalCulture) => originalCulture == "en" ? "Es" : "En";

    public static string ValueHeader(BusinessFormCatalogValueType valueType, string culture, bool canonical) =>
        FormCatalogTexts.Get((canonical ? "HeaderCanonicalValue" : "HeaderValue") + valueType, culture);

    public static string FieldHeader(BusinessFormCatalogField field, string culture, bool canonical) => canonical
        ? Format("HeaderCanonicalField", culture, field.Key)
        : FixedHeaderKey(field.Key) is not null ? ExtraFieldHeader(field.Key, culture) : field.Key;

    // «… [campo]» or «… [field]»: a field's key or name that otherwise would read as a fixed column.
    public static string ExtraFieldHeader(string text, string culture) => Format("HeaderExtraField", culture, text);

    public static string? FixedHeaderKey(string text)
    {
        var key = NormalizeHeader(text);
        if (IsCanonicalValueHeader(text) || key.StartsWith("valor (", StringComparison.Ordinal) || key.StartsWith("value (", StringComparison.Ordinal)) return "value";
        return key switch
        {
            "codigo" or "code" => "code", "texto" or "text" => "text", "texto en ingles" or "english text" => "textEn",
            "texto en espanol" or "spanish text" => "textEs", "detalle" or "detail" => "detail",
            "detalle en ingles" or "english detail" => "detailEn", "detalle en espanol" or "spanish detail" => "detailEs",
            "activa" or "active" => "active", "valor" or "value" => "value", _ => null,
        };
    }

    public static bool IsCanonicalValueHeader(string text)
    {
        var key = NormalizeHeader(text);
        return key.StartsWith("valor canonico (", StringComparison.Ordinal) || key.StartsWith("canonical value (", StringComparison.Ordinal);
    }

    public static string NormalizeHeader(string text) => string.Join(' ', new string(text.Normalize(NormalizationForm.FormD)
        .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark).ToArray())
        .ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string FieldRule(BusinessFormCatalogField field, string culture)
    {
        var rules = new List<string> { FormCatalogTexts.Get(field.Required ? "RuleFieldRequired" : "RuleFieldOptional", culture),
            field.Type == "text" ? Format("RuleFieldText", culture, DisplayNumbers.Number(field.MaxLength ?? 300, culture))
                : FormCatalogTexts.Get("RuleField" + field.Type, culture) };
        if (field.Unique) rules.Add(FormCatalogTexts.Get("RuleFieldUnique", culture));
        if (field.Min is not null) rules.Add(Format("RuleFieldMin", culture, Bound(field.Type, field.Min, culture)));
        if (field.Max is not null) rules.Add(Format("RuleFieldMax", culture, Bound(field.Type, field.Max, culture)));
        return string.Join(' ', rules);
    }

    private static string Format(string key, string culture, string value) =>
        DisplayNumbers.Format(culture, FormCatalogTexts.Get(key, culture), value);

    // A bound as the person writes it: a number with the format of its language and a date with numbers only.
    private static string Bound(string type, string value, string culture) => type switch
    {
        "integer" or "decimal" when decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) =>
            DisplayNumbers.Number(number, culture),
        "date" when DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) =>
            date.ToString(culture == "en" ? "MM/dd/yyyy" : "dd/MM/yyyy", CultureInfo.InvariantCulture),
        _ => value,
    };
}
