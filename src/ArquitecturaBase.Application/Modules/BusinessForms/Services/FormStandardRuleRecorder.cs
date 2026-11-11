using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The rules a standard of the library brings (BI1-A), left by «Agregar» as the first revision of the copy's rules in the same save
/// that creates it: questions by the identities that save gives, an option of a catalog question by the active option with its
/// code (found before the save, as the conditions of the draft) and an option of the question's own, by its identity. They pass the
/// checks of the rule editor against the new draft and the issuers of the standard; a standard whose rules do not is a bug of its
/// module. It does not save on its own.
/// </summary>
internal sealed class FormStandardRuleRecorder(IFormRuleRepository rules, IFormLibraryReader reader, IEnumerable<IStructuredRecordIssuer> issuers)
{
    private const string CodePrefix = "code:";

    /// <summary>What the save runs once it has the new definition; null when the standard brings no rules.</summary>
    public async Task<Result<Action<BusinessFormDefinition, FormIdentityMap>?>> PrepareAsync(StructuredFormStandard standard,
        IReadOnlyDictionary<string, string> catalogKeys, IReadOnlyDictionary<string, Guid> catalogIds,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, Guid>> createdCodes, CancellationToken cancellationToken)
    {
        if (standard.Rules is not { Count: > 0 } standardRules) return Result.Success<Action<BusinessFormDefinition, FormIdentityMap>?>(null);
        var codes = new Dictionary<string, IReadOnlyDictionary<string, Guid>>(StringComparer.Ordinal);
        foreach (var question in standardRules.SelectMany(rule => rule.Conditions).GroupBy(condition => condition.QuestionId, StringComparer.Ordinal))
        {
            var wanted = question.SelectMany(condition => condition.Values ?? []).Where(value => value.StartsWith(CodePrefix, StringComparison.Ordinal))
                .Select(value => value[CodePrefix.Length..]).Distinct(StringComparer.Ordinal).ToArray();
            if (wanted.Length == 0) continue;
            var catalogId = catalogKeys.TryGetValue(question.Key, out var catalogKey) ? catalogIds[catalogKey]
                : throw new InvalidOperationException("A library standard names an option code outside a catalog question in its rules.");
            var options = createdCodes.TryGetValue(catalogId, out var own) ? own : await reader.ListActiveItemIdsByCodeAsync(catalogId, wanted, cancellationToken);
            if (wanted.Any(code => !options.ContainsKey(code))) return FormLibraryErrors.CatalogUnavailable;
            codes[question.Key] = options;
        }
        var available = issuers.Where(issuer => issuer.StandardKey == standard.Key)
            .Select(issuer => new FormRuleIssuerResponse(issuer.IssuerId, issuer.Name, issuer.ResultFields)).ToArray();
        return Result.Success<Action<BusinessFormDefinition, FormIdentityMap>?>((definition, identities) =>
        {
            var ruleSet = Create(definition, standardRules, identities, codes, available);
            rules.Add(ruleSet);
            definition.SetActiveRuleSetId(ruleSet.Id);
        });
    }

    /// <summary>The first revision of the rules of a new definition, checked as the rule editor checks a save.</summary>
    public static BusinessFormRuleSet Create(BusinessFormDefinition definition, IReadOnlyList<StructuredFormStandardRule> standardRules,
        FormIdentityMap identities, IReadOnlyDictionary<string, IReadOnlyDictionary<string, Guid>> codes, IReadOnlyList<FormRuleIssuerResponse> issuers)
    {
        var questions = FormDraftPolicy.Read<FormQuestionSchema>(definition.SchemaJson).Fields;
        var options = new FormRuleOptionsResponse(definition.Id, FormDraftPolicy.Read<Dictionary<string, string>>(definition.NameJson), definition.Culture,
            questions, []) { Issuers = issuers.Count == 0 ? null : issuers };
        var request = new SaveFormRulesRequest(0, standardRules.Select(rule => (FormRuleInput?)new FormRuleInput(Guid.CreateVersion7(), rule.Event,
            rule.Conditions.Select(condition => (FormRecordCondition?)Condition(condition, identities, codes)).ToArray(),
            rule.Actions.Select(action => (FormRuleActionInput?)new FormRuleActionInput(Guid.CreateVersion7(), action.Kind, null,
                action.Text?.ToDictionary(text => text.Key, text => (string?)text.Value), action.IncludePdf, action.IssuerId)).ToArray())).ToArray());
        var normalized = FormRulePolicy.Normalize(request, options, action => FormRulePolicy.Declared(action, options));
        if (normalized.IsFailure) throw new InvalidOperationException("A library standard declares invalid rules: " + normalized.Error.Code);
        return BusinessFormRuleSet.Create(definition.Id, 1, definition.Culture, FormDraftPolicy.Serialize(normalized.Value),
            normalized.Value.SelectMany(rule => rule.Actions).Any(action => action.Kind == "RequestApproval"),
            FormDraftPolicy.Serialize(new FormVersionSchema(1, questions)));
    }

    private static FormRecordCondition Condition(StructuredFormStandardCondition condition, FormIdentityMap identities,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, Guid>> codes)
    {
        var fieldId = identities.Fields.SingleOrDefault(field => field.LocalId == condition.QuestionId)?.FieldId
            ?? throw new InvalidOperationException("A library standard names a question its draft does not have in its rules.");
        return new(fieldId, condition.Operator, condition.Values?.Select(value =>
            value.StartsWith(CodePrefix, StringComparison.Ordinal) ? codes[condition.QuestionId][value[CodePrefix.Length..]].ToString("D")
            : identities.Choices.SingleOrDefault(choice => choice.FieldLocalId == condition.QuestionId && choice.LocalId == value)?.ChoiceId.ToString("D") ?? value).ToArray());
    }
}
