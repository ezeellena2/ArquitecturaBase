using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The catalog's original culture is its only required language: the name, the text of each option and the name of each
/// field need text there, and the other culture is an optional translation. Reports the first missing text at its path.
/// </summary>
internal static class FormCatalogOriginalTextPolicy
{
    public static Result Validate(string original, IReadOnlyDictionary<string, string?>? name,
        IReadOnlyList<FormCatalogManualItemSaveRequest?>? items = null, IReadOnlyList<FormCatalogFieldSaveRequest?>? fields = null)
    {
        if (!HasText(name, original)) return Missing("name." + original);
        items ??= [];
        for (var index = 0; index < items.Count; index++)
            if (!HasText(items[index]?.Text, original)) return Missing($"items[{index}].text.{original}");
        fields ??= [];
        for (var index = 0; index < fields.Count; index++)
            if (!HasText(fields[index]?.Name, original)) return Missing($"fields[{index}].name.{original}");
        return Result.Success();
    }

    private static bool HasText(IReadOnlyDictionary<string, string?>? texts, string culture) =>
        texts is not null && texts.TryGetValue(culture, out var text) && !string.IsNullOrWhiteSpace(text);

    private static ValidationError Missing(string path) => FieldErrors.On(FormCatalogErrors.ContentInvalid, path);
}
