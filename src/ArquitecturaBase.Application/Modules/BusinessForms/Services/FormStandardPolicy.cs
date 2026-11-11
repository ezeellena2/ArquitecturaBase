using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The questions a standard of the library needs (BI1-A), bound in the forms added from it: they keep everything but their text,
/// their help, their place and the value of their toggles (IV2-A). Removing one, or changing its type, block, catalog, options,
/// calculation, condition, rows, toggles or whether it is required, is refused at the question (or at the list, when it is gone).
/// Only a bound question has toggles or an option it starts with (IV1-A, in its source): the standard declares them, so any other
/// question with them is refused.
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
        }
        return Result.Success();
    }

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

    private static string Comparable(CanonicalFormField field) => FormDraftPolicy.Serialize(field with
    {
        Label = NoText, Help = null, Options = field.Options?.Select(option => option with { Label = NoText }).ToArray(),
        Toggles = field.Toggles?.Select(toggle => toggle with { Value = false }).ToArray(),
    });
}
