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

    public static bool HasValidShape(FormCatalogPresentation? presentation) => presentation is null
        || presentation.Title is { Count: > 0 and <= 24 } && presentation.Description is { Count: <= 24 }
            && presentation.Title.Concat(presentation.Description).All(part => part is "text" or "code" or "detail" or "value"
                || Guid.TryParseExact(part, "D", out var id) && id != Guid.Empty);

    public static bool ValidConfiguration(FormCatalogPresentation? presentation, IReadOnlyList<BusinessFormCatalogField> fields, string valueType)
    {
        if (presentation is null) return true;
        if (!HasValidShape(presentation)) return false;
        var allowed = fields.Where(field => field.IsActive).Select(field => field.Id.ToString("D")).ToHashSet(StringComparer.Ordinal);
        allowed.UnionWith(["text", "code", "detail"]);
        if (valueType != "Text") allowed.Add("value");
        return presentation.Title.Concat(presentation.Description).All(allowed.Contains);
    }

    public static bool IsValid(FormCatalogShown shown) => !string.IsNullOrWhiteSpace(shown.Title)
        && shown.Title.Length <= BusinessFormCatalogItem.MaxTextLength && StorableText.IsStorable(shown.Title)
        && (shown.Description is null || shown.Description.Length <= BusinessFormCatalogItem.MaxDetailLength
            && StorableText.IsStorable(shown.Description));

    private static string Join(IReadOnlyList<string> parts, IReadOnlyDictionary<string, string?> values) =>
        string.Join(" · ", parts.Select(part => values.GetValueOrDefault(part)).Where(value => !string.IsNullOrWhiteSpace(value)));
}
