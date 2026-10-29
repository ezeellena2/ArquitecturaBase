using System.Globalization;
using System.Text;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal static class FormCatalogImportTemplate
{
    // «Texto» and «Detalle» are the catalog's original; the other language has its own optional translation column,
    // which `translation: false` leaves out (an absent column keeps the translation on import).
    public static IReadOnlyList<FormCatalogImportTemplateColumn> Columns(BusinessFormCatalogValueType valueType, string culture,
        string originalCulture, bool canonicalValue = false, IReadOnlyList<BusinessFormCatalogField>? fields = null, bool translation = true)
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
            columns.Add(new("value", ValueHeader(valueType, culture, canonicalValue), FormCatalogTexts.Get("RuleValue" + valueType, culture)));
        foreach (var field in (fields ?? []).Where(field => field.IsActive).OrderBy(field => field.Position))
            columns.Add(new(field.Id.ToString("D"), FieldHeader(field, culture, canonicalValue), FieldRule(field, culture)));
        return columns;
    }

    // The suffix of the translation columns: "En" for a Spanish original, "Es" for an English one.
    public static string TranslationSuffix(string originalCulture) => originalCulture == "en" ? "Es" : "En";

    public static string ValueHeader(BusinessFormCatalogValueType valueType, string culture, bool canonical) =>
        FormCatalogTexts.Get((canonical ? "HeaderCanonicalValue" : "HeaderValue") + valueType, culture);

    public static string FieldHeader(BusinessFormCatalogField field, string culture, bool canonical) => canonical
        ? Format("HeaderCanonicalField", culture, field.Key)
        : FixedHeaderKey(field.Key) is not null ? Format("HeaderExtraField", culture, field.Key) : field.Key;

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
            field.Type == "text" ? Format("RuleFieldText", culture, (field.MaxLength ?? 300).ToString(CultureInfo.InvariantCulture))
                : FormCatalogTexts.Get("RuleField" + field.Type, culture) };
        if (field.Unique) rules.Add(FormCatalogTexts.Get("RuleFieldUnique", culture));
        if (field.Min is not null) rules.Add(Format("RuleFieldMin", culture, field.Min));
        if (field.Max is not null) rules.Add(Format("RuleFieldMax", culture, field.Max));
        return string.Join(' ', rules);
    }

    private static string Format(string key, string culture, string value) =>
        string.Format(CultureInfo.InvariantCulture, FormCatalogTexts.Get(key, culture), value);
}
