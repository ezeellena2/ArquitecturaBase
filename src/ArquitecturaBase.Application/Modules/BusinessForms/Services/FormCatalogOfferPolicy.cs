using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal static class FormCatalogOfferPolicy
{
    public static string LocalizeBoolean(string value, string culture) => Resources.FormCatalogTexts.ActiveValue(value, culture);

    public static bool IsInvalid(StructuredFormCatalogOption option) => option.Id == Guid.Empty
        || InvalidText(option.Text, option.Detail)
        || option.Translations is { } translations && (translations.Count == 0
            || translations.Any(pair => pair.Key is not ("es" or "en") || InvalidText(pair.Value.Text, pair.Value.Detail)));

    public static bool IsMissingCulture(StructuredFormCatalogOption option, string? culture) => culture is not null
        && option.Translations is { } translations && !translations.ContainsKey(culture)
        && !(culture == "en" && translations.ContainsKey("es"));

    public static string? Validate(bool required, IReadOnlyList<StructuredFormCatalogOption> options, string? originalCulture = null)
    {
        if (options.Count > 200) return FormRecordErrors.CatalogTooLarge.Code;
        if (required && options.Count == 0) return FormRecordErrors.CatalogEmpty.Code;
        if (options.Any(option => IsInvalid(option) || IsMissingCulture(option, originalCulture))
            || options.Select(option => option.Id).Distinct().Count() != options.Count)
            return FormRecordErrors.CatalogOptionInvalid.Code;
        return null;
    }

    public static bool InvalidNames(IReadOnlyDictionary<string, string>? names) => names is not null
        && (names.Count == 0 || names.Any(pair => pair.Key is not ("es" or "en") || string.IsNullOrWhiteSpace(pair.Value)
            || pair.Value.Length > 200 || !StorableText.IsStorable(pair.Value)));

    private static bool InvalidText(string text, string? detail) => string.IsNullOrWhiteSpace(text)
        || text.Length > BusinessFormCatalogItem.MaxTextLength || !StorableText.IsStorable(text)
        || detail is not null && (detail.Length > BusinessFormCatalogItem.MaxDetailLength || !StorableText.IsStorable(detail));
}
