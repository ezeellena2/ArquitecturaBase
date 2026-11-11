using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal static class FormRulePolicy
{
    // RE1-A: declared says which result data an action returns, so the messages after it in its rule may use them.
    public static Result<IReadOnlyList<FormRule>> Normalize(SaveFormRulesRequest request, FormRuleOptionsResponse options,
        Func<FormRuleAction, IReadOnlyList<FormActionResultDatum>>? declared = null)
    {
        if (request.ExpectedRevision is null or < 0 or >= 9007199254740991) return Invalid("expectedRevision");
        if (request.Rules is not { Count: <= 20 }) return Invalid("rules");
        var ruleIds = new HashSet<Guid>();
        var actionIds = new HashSet<Guid>();
        var normalized = new List<FormRule>();
        var schemaId = options.DefinitionId;
        var definition = new FormResponseDefinitionRow(options.DefinitionId, "", schemaId,
            [new(schemaId, 1, "{}", FormDraftPolicy.Serialize(new FormVersionSchema(1, options.Questions)), options.OriginalCulture, [])]);
        foreach (var (rule, ruleIndex) in request.Rules.Select((rule,index) => (rule,index)))
        {
            var rulePath = $"rules[{ruleIndex}]";
            if (rule is null || rule.Id == Guid.Empty || !ruleIds.Add(rule.Id)) return Invalid(rulePath + ".id");
            if (rule.Event is not ("Received" or "Approved" or "Rejected")) return Invalid(rulePath + ".event");
            if (rule.Conditions is not { Count: <= 10 } || rule.Conditions.Any(condition => condition is null)) return Invalid(rulePath + ".conditions");
            var conditions = rule.Conditions.Select(condition => condition!).ToArray();
            if (conditions.Any(condition => condition.Values is { Count: > 200 } || condition.Values?.Any(value => value is null || value.Length > 600 || !StorableText.IsStorable(value)) == true)) return Invalid(rulePath + ".conditions");
            if (FormRecordFilterPolicy.Validate(new() { Conditions = conditions }, definition).IsFailure) return Invalid(rulePath + ".conditions");
            if (rule.Actions is not { Count: > 0 and <= 10 }) return Invalid(rulePath + ".actions");
            var actions = new List<FormRuleAction>();
            var generatedPdf = false;
            var issued = false;
            var notified = false;
            var resultData = new List<FormActionResultDatum>();
            foreach (var (action, actionIndex) in rule.Actions.Select((action,index) => (action,index)))
            {
                var actionPath = rulePath + $".actions[{actionIndex}]";
                if (action is null || action.Id == Guid.Empty || !actionIds.Add(action.Id)) return Invalid(actionPath + ".id");
                if (action.Kind is not ("RequestApproval" or "GeneratePdf" or "NotifyAuthor" or "NotifyRole" or "IssueDocument" or "SendCopy")) return Invalid(actionPath + ".kind");
                // The copy by email sends the document of an earlier issuance of its rule to its receiver.
                if (action.Kind == "SendCopy" && !issued) return Invalid(actionPath + ".kind");
                // One issuance per rule, on arrival or approval and before the messages that may use its result.
                if (action.Kind == "IssueDocument" && (rule.Event is not ("Received" or "Approved") || issued || notified)) return Invalid(actionPath + ".kind");
                if (action.Kind == "IssueDocument" && options.Issuers?.Any(issuer => issuer.Id == action.IssuerId) != true)
                    return FieldErrors.On(FormRuleErrors.IssuerUnavailable, actionPath + ".issuerId");
                if (action.Kind != "IssueDocument" && action.IssuerId is not null) return Invalid(actionPath + ".issuerId");
                var hasRole = action.Kind is "RequestApproval" or "NotifyRole";
                if (hasRole && (action.RoleId is null || !options.Roles.Any(role => role.Id == action.RoleId))) return FieldErrors.On(FormRuleErrors.RoleUnavailable, actionPath + ".roleId");
                if (!hasRole && action.RoleId is not null) return Invalid(actionPath + ".roleId");
                if (action.Kind == "RequestApproval" && (rule.Event != "Received" || actionIndex != rule.Actions.Count - 1)) return Invalid(actionPath + ".kind");
                if (action.Kind == "GeneratePdf" && rule.Event != "Approved") return Invalid(actionPath + ".kind");
                // The notice to whoever loaded it attaches the PDF of an earlier «Armar el PDF» or the fiscal PDF of an earlier issuance.
                if (action.IncludePdf && (action.Kind != "NotifyAuthor" || !(generatedPdf || issued))) return Invalid(actionPath + ".includePdf");
                Dictionary<string,string>? text = null;
                if (action.Kind is "NotifyAuthor" or "NotifyRole" or "SendCopy")
                {
                    var limit = action.IncludePdf ? 1024 : 4096;
                    if (action.Text is null || action.Text.Keys.Any(culture => culture is not ("es" or "en"))
                        || !action.Text.TryGetValue(options.OriginalCulture, out var original) || string.IsNullOrWhiteSpace(original)) return Invalid(actionPath + ".text." + options.OriginalCulture);
                    text = new(StringComparer.Ordinal);
                    foreach (var (culture,value) in action.Text)
                    {
                        if (string.IsNullOrWhiteSpace(value)) continue;
                        var content = value.Trim();
                        if (content.Length > limit || !StorableText.IsStorable(content)) return Invalid(actionPath + ".text." + culture);
                        if (!ValidPlaceholders(content, culture, options.Questions, resultData)) return FieldErrors.On(FormRuleErrors.PlaceholderInvalid, actionPath + ".text." + culture);
                        text.Add(culture, content);
                    }
                }
                else if (action.Text is not null) return Invalid(actionPath + ".text");
                actions.Add(new(action.Id, action.Kind, action.RoleId, text, action.IncludePdf, action.IssuerId));
                if (declared is not null) resultData.AddRange(declared(actions[^1]));
                generatedPdf |= action.Kind == "GeneratePdf";
                issued |= action.Kind == "IssueDocument";
                notified |= action.Kind is "NotifyAuthor" or "NotifyRole" or "SendCopy";
            }
            normalized.Add(new(rule.Id, rule.Event, conditions.Select(condition => condition with { Values = condition.Values?.ToArray() }).ToArray(), actions));
        }
        return normalized;
    }
    /// <summary>The data an «IssueDocument» action declares: the result fields of its issuer, with the issuer as their source.</summary>
    public static IReadOnlyList<FormActionResultDatum> Declared(FormRuleAction action, FormRuleOptionsResponse options) =>
        action.Kind == "IssueDocument" && options.Issuers?.FirstOrDefault(issuer => issuer.Id == action.IssuerId) is { } issuer
            ? issuer.ResultFields.Select(field => new FormActionResultDatum(issuer.Id, field.Key)).ToArray() : [];
    // A datum of a result (RE1-A) is valid only when an earlier action of the same rule declares it.
    internal static bool ValidPlaceholders(string text, string culture, IReadOnlyList<CanonicalFormField> questions,
        IReadOnlyList<FormActionResultDatum> resultData)
    {
        var position = 0;
        while (position < text.Length)
        {
            if (text[position] == '}') return false;
            if (text[position++] != '{') continue;
            var end = text.IndexOf('}', position);
            if (end < 0) return false;
            var token = text[position..end];
            if (FormActionResultTokenPolicy.IsResult(token))
            {
                if (!FormActionResultTokenPolicy.TryParse(token, out var datum) || !resultData.Contains(datum)) return false;
            }
            else if (token is not ("número" or "number" or "motivo" or "reason")
                && (Question(token, culture, questions) is not { } question || question.Repeated == true)) return false;
            position = end + 1;
        }
        return true;
    }
    /// <summary>
    /// The question of a token, by its identity or by its only label in the culture of the text. A question of the block of rows
    /// is not a datum: its block is, and writes its answers item by item (RG5-A).
    /// </summary>
    private static CanonicalFormField? Question(string token, string culture, IReadOnlyList<CanonicalFormField> questions)
    {
        if (FormActionMessagePolicy.Reference(token) is { } fieldId) return questions.FirstOrDefault(question => question.FieldId == fieldId);
        var named = questions.Where(question => question.Label.GetValueOrDefault(culture) == token).ToArray();
        return named.Length == 1 ? named[0] : null;
    }
    private static ValidationError Invalid(string path) => FieldErrors.On(FormRuleErrors.ContentInvalid, path);
}
