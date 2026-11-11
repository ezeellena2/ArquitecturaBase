using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal static class FormRecordReferencePolicy
{
    /// <summary>Each answer with its item of the block, null outside it; the block itself is not a value.</summary>
    public static IEnumerable<(FormRecordAnswer Answer, int? Item)> Placed(IEnumerable<FormRecordAnswer> answers) => answers.SelectMany(Place);

    /// <summary>The options chosen in catalog questions, in the order of the answers: each with its item of the block and the offer of its question.</summary>
    public static IReadOnlyList<FormRecordChosenOption> Chosen(FormVersionSchema schema, IEnumerable<FormRecordAnswer> answers,
        IReadOnlyList<StructuredFormCatalogOffer> offers) =>
        Placed(answers).Where(value => schema.Questions.Single(question => question.FieldId == value.Answer.FieldId).Source?.Catalog is not null)
            .SelectMany(value => (value.Answer.ChoiceIds ?? [value.Answer.ChoiceId!.Value]).Select(choiceId => new FormRecordChosenOption(
                value.Answer.FieldId, value.Item, choiceId, offers.Single(offer => offer.FieldId == value.Answer.FieldId))))
            .ToArray();

    /// <summary>The options of a catalog with items (Manual or External), each once: those whose fields are read to seal them.</summary>
    public static IReadOnlyList<Guid> ItemIds(IEnumerable<FormRecordChosenOption> chosen) =>
        chosen.Where(value => FormCatalogKindPolicy.HasItems(value.Offer.Kind)).Select(value => value.ChoiceId).Distinct().ToArray();

    /// <summary>
    /// What a response keeps of each option chosen, the same for the capture and for «¿La emito?» before «Enviar»: the option's
    /// text, detail and value as offered, its translations, the fields captured of a manual catalog's option and the item of
    /// the block (0 outside it). <paramref name="fields"/> are the ones <see cref="FormCatalogFieldCapturePolicy"/> captured.
    /// </summary>
    public static IReadOnlyList<FormRecordReferenceRow> References(Guid recordId, IEnumerable<FormRecordChosenOption> chosen,
        IReadOnlyDictionary<Guid, string> fields) => chosen.Select(value =>
        {
            var option = value.Offer.Options.Single(item => item.Id == value.ChoiceId);
            return new FormRecordReferenceRow(recordId, value.FieldId, value.Offer.CatalogId, value.Offer.Revision, option.Id, option.Text,
                option.Detail, value.Offer.CatalogName, Capture(option, value.Offer), option.Value, value.Offer.ValueType,
                value.Offer.Kind == "Manual" ? fields.GetValueOrDefault(option.Id) : null, value.Item ?? 0);
        }).ToArray();

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
        // A captured option has no translation only in its catalog's original language, so the other culture is that original.
        var original = translations?.GetValueOrDefault(culture == "en" ? "es" : "en");
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

    public static IReadOnlyList<string> CompleteCultures(FormActionRecordRow snapshot, IReadOnlySet<Guid> usedFields)
    {
        var references = (snapshot.Response.Record.References ?? []).Where(reference => usedFields.Contains(reference.FieldId))
            .Select(Read).ToArray();
        // A reference shows its catalog's original when the culture has no translation, so any captured text completes every culture.
        var complete = references.All(translations => translations is null
            || translations.Values.Any(text => !string.IsNullOrWhiteSpace(text.Text)));
        return complete ? new[] { snapshot.OriginalCulture, snapshot.OriginalCulture == "es" ? "en" : "es" } : [];
    }

    private static Dictionary<string, FormRecordReferenceTextResponse>? Read(FormRecordReferenceRow reference) =>
        reference.TranslationsJson is null ? null
            : FormDraftPolicy.Read<Dictionary<string, FormRecordReferenceTextResponse>>(reference.TranslationsJson);

    private static IEnumerable<(FormRecordAnswer Answer, int? Item)> Place(FormRecordAnswer answer) => answer.Rows is { } rows
        ? rows.SelectMany((items, row) => items.Select(item => (item, (int?)row)))
        : [(answer, null)];
}

/// <summary>An option chosen in a catalog question: its question, the item of the block it was chosen in (null outside it) and its offer.</summary>
internal sealed record FormRecordChosenOption(Guid FieldId, int? Item, Guid ChoiceId, StructuredFormCatalogOffer Offer)
{
    public override string ToString() => "FormRecordChosenOption { Content = [REDACTED] }";
}
