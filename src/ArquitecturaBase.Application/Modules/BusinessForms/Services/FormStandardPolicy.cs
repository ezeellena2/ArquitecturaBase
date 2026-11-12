using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The questions a standard of the library needs (BI1-A), bound in the forms added from it: they keep everything but their text,
/// their help, their place, the value of their toggles (IV2-A) and what they receive (W3). Removing one, or changing its type, block,
/// catalog, options, calculation, condition, rows, toggles or whether it is required, is refused at the question (or at the list, when
/// it is gone).
/// Only a bound question has toggles or an option it starts with (IV1-A, in its source): the standard declares them, so any other
/// question with them is refused. The same goes for a fixed validation (DOC1), which also does not change.
/// </summary>
internal static class FormStandardPolicy
{
    private static readonly IReadOnlyDictionary<string, string> NoText = new Dictionary<string, string>();

    /// <summary>Which question holds each datum, once the new draft has its field ids.</summary>
    public static IReadOnlyDictionary<string, Guid> Bindings(FormDraftStandardOrigin standard, FormIdentityMap identities) =>
        new SortedDictionary<string, Guid>(standard.Bindings.ToDictionary(pair => pair.Value,
            pair => identities.Fields.Single(field => field.LocalId == pair.Key).FieldId), StringComparer.Ordinal);

    public static FormDefinitionStandardResponse? Response(string? key, int? version, string? bindingsJson) =>
        key is null || version is null || bindingsJson is null ? null
            : new(key, version.Value, FormDraftPolicy.Read<Dictionary<string, Guid>>(bindingsJson));

    public static Result CheckBound(string bindingsJson, IReadOnlyList<CanonicalFormField> previous, IReadOnlyList<CanonicalFormField> current)
    {
        foreach (var fieldId in FormDraftPolicy.Read<Dictionary<string, Guid>>(bindingsJson).Values)
        {
            if (previous.FirstOrDefault(field => field.FieldId == fieldId) is not { } before) continue;
            var index = current.ToList().FindIndex(field => field.FieldId == fieldId);
            if (index < 0) return FieldErrors.On(FormDraftErrors.BoundQuestionChanged, "draft.fields");
            if (Comparable(before) != Comparable(current[index])) return FieldErrors.On(FormDraftErrors.BoundQuestionChanged, $"draft.fields[{index}]");
        }
        return Result.Success();
    }

    public static Result CheckDeclared(IReadOnlyCollection<Guid> bound, IReadOnlyList<CanonicalFormField> current)
    {
        for (var index = 0; index < current.Count; index++)
        {
            if (bound.Contains(current[index].FieldId)) continue;
            if (current[index].Toggles is not null) return FieldErrors.On(FormDraftErrors.QuestionConfigurationInvalid, $"draft.fields[{index}].toggles");
            if (current[index].Source?.Default is not null) return FieldErrors.On(FormDraftErrors.QuestionConfigurationInvalid, $"draft.fields[{index}].source.default");
            if (current[index].Validation is not null) return FieldErrors.On(FormDraftErrors.QuestionConfigurationInvalid, $"draft.fields[{index}].validation");
        }
        return Result.Success();
    }

    // DOC1: what the library says of a standard that reads documents: the data it reads (its bound questions, files aside, each by its
    // datum key and in the standard's order) and its fixed validations, by the names of their questions in the menu language. Null
    // without them, so a standard keeps its bytes.
    public static IReadOnlyList<FormLibraryReadResponse>? Reads(StructuredFormStandard standard, string culture) => standard.Reading is null ? null
        : Bound(standard).Where(field => field.Type is not ("photo" or "document" or "group"))
            .Select(field => new FormLibraryReadResponse(standard.Bindings[field.LocalId], FormRecordPresentationPolicy.Text(field.Label, culture)))
            .ToArray() is { Length: > 0 } reads ? reads : null;

    public static IReadOnlyList<FormLibraryValidationResponse>? Validations(StructuredFormStandard standard, string culture) =>
        Bound(standard).Where(field => field.Validation is not null)
            .Select(field => new FormLibraryValidationResponse(FormRecordPresentationPolicy.Text(field.Label, culture), field.Validation!))
            .ToArray() is { Length: > 0 } validations ? validations : null;

    // DOC1: «Agregar» copies the reading of the standard into the new draft: on, with «Cómo reconocerlo» in the copy's language and
    // without lookups (the bank comes from the account, not from a lookup).
    public static FormDocumentReading? Reading(StructuredFormStandard standard, string culture) => standard.Reading is { } reading
        ? new(true, FormRecordPresentationPolicy.Text(reading.Recognition, culture), []) : null;

    private static IEnumerable<FormFieldInput> Bound(StructuredFormStandard standard) =>
        FormDraftPolicy.Read<FormDraftInput>(standard.DraftJson).Fields.Where(field => standard.Bindings.ContainsKey(field.LocalId));

    // BI1-A: the prefix «Agregar» offers: the standard's or, when a form already has it, the first with a number after it that no
    // form uses (deleted ones too, as saving checks), within the twelve characters of a prefix.
    public static string SuggestedPrefix(string prefix, IReadOnlySet<string> taken)
    {
        if (!taken.Contains(prefix)) return prefix;
        for (var number = 2; ; number++)
        {
            var suffix = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var candidate = prefix[..Math.Min(prefix.Length, 12 - suffix.Length)] + suffix;
            if (!taken.Contains(candidate)) return candidate;
        }
    }

    // What a question receives by a channel (W3) is the form's choice, not the standard's: a bound text question can take it.
    private static string Comparable(CanonicalFormField field) => FormDraftPolicy.Serialize(field with
    {
        Label = NoText, Help = null, Options = field.Options?.Select(option => option with { Label = NoText }).ToArray(),
        Toggles = field.Toggles?.Select(toggle => toggle with { Value = false }).ToArray(), Receives = null,
    });
}
