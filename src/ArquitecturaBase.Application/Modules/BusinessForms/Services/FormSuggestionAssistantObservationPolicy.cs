using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal static class FormSuggestionAssistantObservationPolicy
{
    public static bool ObservedSource(AssistantCapabilityRequest request, Guid recipient, Guid sourceRecordId)
    {
        var reads = request.ObservedReads.Where(read => read.TurnId == request.TurnId
            && read.Capability.Id == BusinessFormSuggestionAssistantCapabilitySource.OffersReadId).ToArray();
        if (reads.Length == 0 || reads.Select(read => read.Ordinal).Distinct().Count() != reads.Length) return false;
        var latest = reads.MaxBy(read => read.Ordinal)!;
        return latest.Ordinal >= 1 && latest.Capability.Version == 1
            && latest.Payload is StructuredFormOffersPayload payload && payload.SubjectUserId == recipient
            && payload.Culture == request.Context.Culture && payload.CapturedSourceRecordIds.Contains(sourceRecordId);
    }

    public static Result<FormObservedAssistantSuggestion> Find(IReadOnlyList<AssistantObservedRead> reads, Guid turnId,
        Guid recipient, string culture, Guid sourceRecordId, Guid suggestionId)
    {
        var matching = reads.Where(read => read.TurnId == turnId
            && read.Capability.Id == BusinessFormSuggestionAssistantCapabilitySource.CandidatesId).ToArray();
        if (matching.Length == 0 || matching.Select(read => read.Ordinal).Distinct().Count() != matching.Length) return FormRecordErrors.InvalidResponse;
        var latest = matching.MaxBy(read => read.Ordinal)!;
        if (latest.Ordinal < 1 || latest.Capability.Version != BusinessFormSuggestionAssistantCapabilitySource.Version
            || latest.Payload is not FormSuggestionAssistantCandidatesPayload payload || !FormSuggestionAssistantCapabilityCodec.ValidRead(payload)
            || payload.RecipientUserId != recipient || payload.Culture != culture || payload.SourceRecordId != sourceRecordId)
            return FormRecordErrors.InvalidResponse;
        var suggestion = payload.Suggestions.SingleOrDefault(row => row.SuggestionId == suggestionId);
        return suggestion is null || payload.SuggestionSetId is null ? FormRecordErrors.InvalidResponse
            : new FormObservedAssistantSuggestion(latest.Ordinal, payload, suggestion);
    }

    public static Result<IReadOnlyList<FormAssistantValue>> Normalize(FormObservedAssistantSuggestion observed,
        IReadOnlyList<FormAssistantValue> supplied, string culture)
    {
        var normalized = FormAssistantObservationPolicy.Normalize(observed.Suggestion.Form, supplied, culture);
        if (normalized.IsFailure) return normalized.Error;
        var values = observed.Suggestion.MappedValues.ToList();
        foreach (var value in normalized.Value)
        {
            var mapped = values.SingleOrDefault(field => field.FieldId == value.FieldId);
            if (mapped is not null && mapped != value) return FormRecordErrors.InvalidResponse;
            if (mapped is null) values.Add(value);
        }
        return values;
    }

    public static Result<FormObservedAssistantSuggestion> Validate(AssistantObservedProposal proposal, Guid recipient,
        string culture, Guid sourceRecordId, Guid suggestionSetId, Guid suggestionId)
    {
        if (proposal.Id == Guid.Empty || proposal.TurnId == Guid.Empty
            || proposal.Capability is not { Id: BusinessFormSuggestionAssistantCapabilitySource.RegistrationId, Version: BusinessFormSuggestionAssistantCapabilitySource.Version }
            || proposal.Payload is not FormSuggestionAssistantRegistrationPayload payload || !FormSuggestionAssistantCapabilityCodec.ValidProposal(payload)
            || payload.SourceRecordId != sourceRecordId || payload.SuggestionSetId != suggestionSetId || payload.SuggestionId != suggestionId)
            return FormRecordErrors.InvalidResponse;
        var observed = Find(proposal.ObservedReads, proposal.TurnId, recipient, culture, sourceRecordId, suggestionId);
        if (observed.IsFailure) return observed.Error;
        var read = observed.Value;
        if (payload.ReadOrdinal != read.ReadOrdinal || payload.SuggestionSetId != read.Payload.SuggestionSetId
            || payload.ExpectedSuggestionRevision != read.Payload.SuggestionRevision
            || payload.DefinitionId != read.Suggestion.Form.DefinitionId || payload.VersionId != read.Suggestion.Form.VersionId
            || payload.ExpectedDefinitionRevision != read.Suggestion.Form.DefinitionRevision) return FormRecordErrors.InvalidResponse;
        var values = Normalize(read, payload.Values, culture);
        if (values.IsFailure || values.Value.Count != payload.Values.Count
            || values.Value.Any(value => !payload.Values.Contains(value))) return FormRecordErrors.InvalidResponse;
        return read;
    }
}

internal sealed record FormObservedAssistantSuggestion(int ReadOrdinal, FormSuggestionAssistantCandidatesPayload Payload,
    FormSuggestionAssistantCandidate Suggestion)
{ public override string ToString() => "FormObservedAssistantSuggestion { Content = [REDACTED] }"; }
