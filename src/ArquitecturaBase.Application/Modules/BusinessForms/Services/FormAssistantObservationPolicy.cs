using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal static class FormAssistantObservationPolicy
{
    public static Result<FormObservedAssistantCandidate> Find(IReadOnlyList<AssistantObservedRead> reads, Guid turnId,
        Guid recipient, string culture, Guid definitionId, Guid versionId)
    {
        var matching = reads.Where(read => read.TurnId == turnId && read.Capability.Id == BusinessFormAssistantCapabilitySource.CandidatesId).ToArray();
        if (matching.Length == 0 || matching.Select(read => read.Ordinal).Distinct().Count() != matching.Length) return FormRecordErrors.InvalidResponse;
        var latest = matching.MaxBy(read => read.Ordinal)!;
        if (latest.Ordinal < 1 || latest.Capability.Version != BusinessFormAssistantCapabilitySource.Version
            || latest.Payload is not FormAssistantCandidatesPayload payload || !FormAssistantCapabilityCodec.ValidRead(payload)
            || payload.RecipientUserId != recipient || payload.Culture != culture) return FormRecordErrors.InvalidResponse;
        var form = payload.Forms.SingleOrDefault(form => form.DefinitionId == definitionId && form.VersionId == versionId);
        return form is null ? FormRecordErrors.InvalidResponse : new FormObservedAssistantCandidate(latest.Ordinal, form);
    }

    public static Result<IReadOnlyList<FormAssistantValue>> Normalize(FormAssistantCandidate form, IReadOnlyList<FormAssistantValue> values, string culture)
    {
        if (!FormAssistantCapabilityCodec.ValidArgumentValues(values)) return FormRecordErrors.InvalidResponse;
        var normalized = new List<FormAssistantValue>();
        foreach (var value in values)
        {
            var question = form.Questions.SingleOrDefault(question => question.FieldId == value.FieldId);
            if (question is null || question.Type != value.Type) return FormRecordErrors.InvalidResponse;
            // Membership comes exclusively from the one observed owner snapshot, including catalog choices.
            var schema = new StructuredFormQuestion(question.FieldId, "field_" + question.FieldId.ToString("N"), question.Type,
                new Dictionary<string, string> { [culture] = question.Label }, null, false, question.MaxLength,
                question.Type == "choice" ? question.Choices.Select(choice => new StructuredFormChoice(choice.Id,
                    new Dictionary<string, string> { [culture] = choice.Text })).ToArray() : null);
            var canonical = FormScalarPrefillPolicy.Normalize(schema, value.Value, []);
            if (canonical.IsFailure)
            {
                // A foreign choice is an authority violation, rather than a scalar formatting mistake.
                if (value.Type == "choice") return canonical.Error;
                continue;
            }
            normalized.Add(value with { Value = canonical.Value });
        }
        return normalized;
    }
}

internal sealed record FormObservedAssistantCandidate(int ReadOrdinal, FormAssistantCandidate Form)
{ public override string ToString() => "FormObservedAssistantCandidate { Content = [REDACTED] }"; }
