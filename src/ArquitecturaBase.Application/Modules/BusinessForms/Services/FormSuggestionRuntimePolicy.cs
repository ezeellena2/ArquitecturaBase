using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormSuggestionRuntimePolicy(FormSuggestionCandidatePolicy candidatePolicy, FormCatalogPreparationPolicy preparation,
    IFormSuggestionReader suggestions, FormAssistantCandidatePolicy candidates)
{
    public Task<Result<StructuredFormSuggestionsResponse>> ListAsync(Guid recipientUserId, string culture, Guid sourceRecordId, CancellationToken ct) =>
        candidatePolicy.ListAsync(recipientUserId, culture, sourceRecordId, ct);

    public async Task<Result<StructuredFormSuggestionPreparationResponse>> PrepareAsync(StructuredFormSuggestionPreparationRequest request, CancellationToken ct)
    {
        var loaded = await candidatePolicy.LoadAsync(request.RecipientUserId, request.Culture, request.SourceRecordId, ct);
        if (loaded.IsFailure) return loaded.Error;
        var context = loaded.Value;
        if (context.Response.SuggestionSetId != request.SuggestionSetId) return FormSuggestionErrors.RevisionConflict;
        var candidate = context.Response.Candidates.SingleOrDefault(candidate => candidate.SuggestionId == request.SuggestionId);
        var suggestion = context.Suggestions.SingleOrDefault(suggestion => suggestion.Id == request.SuggestionId);
        if (candidate is null || suggestion is null) return FormRecordErrors.InvalidResponse;
        FormObservedAssistantSuggestion? observed = null;
        FormSuggestionAssistantRegistrationPayload? payload = null;
        if (candidate.Mode == "Always")
        {
            if (request.Proposal is not null) return FormRecordErrors.InvalidResponse;
        }
        else
        {
            if (request.Proposal is null) return FormRecordErrors.InvalidResponse;
            var validation = FormSuggestionAssistantObservationPolicy.Validate(request.Proposal, request.RecipientUserId, request.Culture,
                request.SourceRecordId, request.SuggestionSetId, request.SuggestionId);
            if (validation.IsFailure) return validation.Error;
            observed = validation.Value;
            payload = (FormSuggestionAssistantRegistrationPayload)request.Proposal.Payload;
            if (candidate.Form.DefinitionId != payload.DefinitionId || candidate.Form.VersionId != payload.VersionId
                || candidate.DefinitionRevision != payload.ExpectedDefinitionRevision) return FormSuggestionErrors.RevisionConflict;
        }
        var catalogs = candidate.Catalogs;
        if (request.ForSending)
        {
            var prepared = await preparation.PrepareAsync(request.RecipientUserId, request.Culture, candidate.Form.DefinitionId,
                candidate.Form.VersionId, true, ct);
            if (!prepared.CanSend) return FormRecordErrors.InvalidResponse;
            catalogs = prepared.Catalogs;
        }
        var current = new FormPreparedAssistantCandidate(candidate.Form, candidate.DefinitionRevision, "", candidate.OriginalCulture,
            candidate.Questions, catalogs);
        var culture = FormAssistantCandidatePolicy.PresentationCulture(current, request.Culture);
        var mapped = FormSuggestionMappingPolicy.Map(suggestion, context.Source.Record, current.Questions, current.Catalogs);
        IReadOnlyDictionary<string, string> values = mapped;
        if (payload is not null)
        {
            var validated = ValidateValues(payload, observed!, current, mapped);
            if (validated.IsFailure) return validated.Error;
            values = validated.Value;
        }
        var proposed = FormSuggestionCandidatePolicy.Fields(current, values, culture);
        var included = request.IncludedFieldIds ?? proposed.Select(field => field.FieldId).ToArray();
        if (included.Distinct().Count() != included.Count || included.Any(id => !proposed.Any(field => field.FieldId == id)))
            return FormRecordErrors.InvalidResponse;
        if (!await candidates.IsEligibleAsync(request.RecipientUserId, request.Culture, ct)) return FormRecordErrors.AuthorizationChanged;
        var final = await suggestions.FindCurrentAsync(context.Source.Record.DefinitionId, ct);
        if (final?.Id != request.SuggestionSetId) return FormSuggestionErrors.RevisionConflict;
        var originMappings = suggestion.Mappings.Where(mapping => proposed.Any(field => field.FieldId == mapping.DestinationFieldId))
            .Select(mapping => new StructuredRecordSuggestionMapping(mapping.Source.Kind, mapping.Source.FieldId, mapping.DestinationFieldId)).ToArray();
        var reason = payload?.Reason ?? FormAssistantTexts.SuggestedAfterResponse(culture, context.Response.SourceNumber);
        return new StructuredFormSuggestionPreparationResponse(new(current.Form, current.DefinitionRevision,
            proposed.Where(field => included.Contains(field.FieldId)).ToArray(), current.Catalogs, reason, culture), proposed,
            new(request.SourceRecordId, suggestion.Id, request.SuggestionSetId, originMappings), candidate.CreatesAutomaticLoop);
    }

    private static Result<IReadOnlyDictionary<string, string>> ValidateValues(FormSuggestionAssistantRegistrationPayload payload,
        FormObservedAssistantSuggestion observed, FormPreparedAssistantCandidate current, IReadOnlyDictionary<string, string> mapped)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var value in payload.Values)
        {
            var question = current.Questions.SingleOrDefault(question => question.FieldId == value.FieldId);
            var previous = observed.Suggestion.Form.Questions.SingleOrDefault(question => question.FieldId == value.FieldId);
            if (question is null || previous is null || question.Type != value.Type || question.SourceCatalogId != previous.CatalogId)
                return FormRecordErrors.InvalidResponse;
            var catalog = current.Catalogs.SingleOrDefault(catalog => catalog.FieldId == value.FieldId);
            if (catalog is not null && catalog.Revision != previous.CatalogRevision) return FormRecordErrors.InvalidResponse;
            var canonical = FormScalarPrefillPolicy.Normalize(question, value.Value, current.Catalogs);
            if (canonical.IsFailure) return canonical.Error;
            values.Add(question.Key, canonical.Value);
        }
        if (mapped.Any(pair => !values.TryGetValue(pair.Key, out var value) || value != pair.Value)) return FormRecordErrors.InvalidResponse;
        return values;
    }
}
