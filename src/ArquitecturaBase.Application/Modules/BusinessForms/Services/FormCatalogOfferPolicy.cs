using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal static class FormCatalogOfferPolicy
{
    public static string LocalizeBoolean(string value, string culture) => Resources.FormCatalogTexts.ActiveValue(value, culture);

    public static string? Validate(bool required, IReadOnlyList<StructuredFormCatalogOption> options)
    {
        if (options.Count > 200) return FormRecordErrors.CatalogTooLarge.Code;
        if (required && options.Count == 0) return FormRecordErrors.CatalogEmpty.Code;
        if (options.Any(option => option.Id == Guid.Empty || string.IsNullOrWhiteSpace(option.Text)
            || option.Text.Length > 30 || option.Detail?.Length > 300)
            || options.Select(option => option.Id).Distinct().Count() != options.Count)
            return FormRecordErrors.CatalogOptionInvalid.Code;
        return null;
    }
}
