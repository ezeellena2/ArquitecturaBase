using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal static class FormRecordReferencePolicy
{
    public static string? Capture(StructuredFormCatalogOption option, StructuredFormCatalogOffer offer)
    {
        if (option.Translations is null) return null;
        var translations = option.Translations.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => new FormRecordReferenceTextResponse(pair.Value.Text,
                pair.Value.Detail, offer.CatalogNames?.GetValueOrDefault(pair.Key) ?? offer.CatalogName), StringComparer.Ordinal);
        return FormDraftPolicy.Serialize(translations);
    }

    public static FormRecordReferenceTextResponse Text(FormRecordReferenceRow reference, string culture)
    {
        var translations = Read(reference);
        var original = translations?.GetValueOrDefault("es");
        var text = translations?.GetValueOrDefault(culture)
            ?? new FormRecordReferenceTextResponse(original?.Text ?? reference.Text, original?.Detail ?? reference.Detail, reference.CatalogName);
        return text with { Detail = !string.IsNullOrWhiteSpace(text.Detail) ? text.Detail : original?.Detail };
    }

    public static FormResponseStoredRow Localize(FormResponseStoredRow response, string culture) => response with
    {
        Record = response.Record with
        {
            References = response.Record.References?.Select(reference =>
            {
                var text = Text(reference, culture);
                return reference with { Text = text.Text, Detail = text.Detail, CatalogName = text.CatalogName };
            }).ToArray()
        }
    };

    public static IReadOnlyList<string> CompleteCultures(FormActionRecordSnapshot snapshot, IReadOnlySet<Guid> usedFields)
    {
        var references = (snapshot.Response.Record.References ?? []).Where(reference => usedFields.Contains(reference.FieldId))
            .Select(Read).ToArray();
        return new[] { snapshot.OriginalCulture, snapshot.OriginalCulture == "es" ? "en" : "es" }
            .Where(culture => references.All(translations => translations is null
                || translations.TryGetValue(culture, out var text) && !string.IsNullOrWhiteSpace(text.Text)
                || culture == "en" && translations.TryGetValue("es", out var original) && !string.IsNullOrWhiteSpace(original.Text))).ToArray();
    }

    private static Dictionary<string, FormRecordReferenceTextResponse>? Read(FormRecordReferenceRow reference) =>
        reference.TranslationsJson is null ? null
            : FormDraftPolicy.Read<Dictionary<string, FormRecordReferenceTextResponse>>(reference.TranslationsJson);
}
