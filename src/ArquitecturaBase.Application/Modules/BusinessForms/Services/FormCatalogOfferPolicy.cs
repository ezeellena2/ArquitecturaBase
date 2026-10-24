using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal static class FormCatalogOfferPolicy
{
    public static string LocalizeBoolean(string value, string culture) => Resources.FormCatalogTexts.ActiveValue(value, culture);

    public static bool IsInvalid(StructuredFormCatalogOption option) => option.Id == Guid.Empty
        || InvalidText(option.Text, option.Detail)
        || option.Shown is { } shown && InvalidText(shown.Title, shown.Description)
        || option.Translations is { } translations && (translations.Count == 0
            || translations.Any(pair => pair.Key is not ("es" or "en") || InvalidText(pair.Value.Text, pair.Value.Detail)));

    // An option needs no text in the form's language: it carries its catalog's original, which is shown when the translation is missing.
    public static string? Validate(bool required, IReadOnlyList<StructuredFormCatalogOption> options, FormSelectionSettings? selection = null)
    {
        if (options.Count > (selection is null ? 200 : 20))
            return selection is null ? FormRecordErrors.CatalogTooLarge.Code : FormRecordErrors.CatalogTooLargeForMultiple.Code;
        if (required && options.Count == 0) return FormRecordErrors.CatalogEmpty.Code;
        if (selection is not null && options.Count < selection.MinSelected) return FormRecordErrors.CatalogInsufficientOptions.Code;
        if (options.Any(IsInvalid)
            || options.Select(option => option.Id).Distinct().Count() != options.Count)
            return FormRecordErrors.CatalogOptionInvalid.Code;
        return null;
    }

    public static bool InvalidValues(StructuredFormCatalogOffer offer) =>
        offer.ValueType is not null && (offer.Kind != "Manual" || offer.ValueType is not ("Integer" or "Decimal" or "Date"))
        || offer.Options.Any(option => InvalidValue(option, offer.ValueType));

    public static bool InvalidValue(StructuredFormCatalogOption option, string? valueType)
    {
        if (valueType is null) return option.Value is not null;
        var type = valueType switch
        {
            "Integer" => BusinessFormCatalogValueType.Integer,
            "Decimal" => BusinessFormCatalogValueType.Decimal,
            "Date" => BusinessFormCatalogValueType.Date,
            _ => BusinessFormCatalogValueType.Text,
        };
        if (type == BusinessFormCatalogValueType.Text) return true;
        var normalized = FormCatalogValuePolicy.Normalize(type, option.Value);
        return normalized.IsFailure || normalized.Value != option.Value;
    }

    public static bool InvalidNames(IReadOnlyDictionary<string, string>? names) => names is not null
        && (names.Count == 0 || names.Any(pair => pair.Key is not ("es" or "en") || string.IsNullOrWhiteSpace(pair.Value)
            || pair.Value.Length > 200 || !StorableText.IsStorable(pair.Value)));

    private static bool InvalidText(string text, string? detail) => string.IsNullOrWhiteSpace(text)
        || text.Length > BusinessFormCatalogItem.MaxTextLength || !StorableText.IsStorable(text)
        || detail is not null && (detail.Length > BusinessFormCatalogItem.MaxDetailLength || !StorableText.IsStorable(detail));
}
