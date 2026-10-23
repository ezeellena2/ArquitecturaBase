using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal static class FormCatalogPresentationPolicy
{
    public static FormCatalogShown Compose(FormCatalogPresentation? presentation, IReadOnlyDictionary<string, string?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var title = Join(presentation?.Title ?? ["text"], values);
        var description = Join(presentation?.Description ?? ["detail"], values);
        return new(title, description.Length == 0 ? null : description);
    }

    public static bool IsValid(FormCatalogShown shown) => !string.IsNullOrWhiteSpace(shown.Title)
        && shown.Title.Length <= BusinessFormCatalogItem.MaxTextLength && StorableText.IsStorable(shown.Title)
        && (shown.Description is null || shown.Description.Length <= BusinessFormCatalogItem.MaxDetailLength
            && StorableText.IsStorable(shown.Description));

    private static string Join(IReadOnlyList<string> parts, IReadOnlyDictionary<string, string?> values) =>
        string.Join(" · ", parts.Select(part => values.GetValueOrDefault(part)).Where(value => !string.IsNullOrWhiteSpace(value)));
}
