using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The questions a standard of the library needs (BI1-A), bound in the forms added from it: they keep everything but their text,
/// their help and their place. Removing one, or changing its type, block, catalog, options, calculation, condition, rows or whether
/// it is required, is refused at the question (or at the list, when it is gone).
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

    private static string Comparable(CanonicalFormField field) => FormDraftPolicy.Serialize(field with
    {
        Label = NoText, Help = null, Options = field.Options?.Select(option => option with { Label = NoText }).ToArray(),
    });
}
