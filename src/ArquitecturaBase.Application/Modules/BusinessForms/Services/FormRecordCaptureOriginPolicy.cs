using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormRecordCaptureOriginPolicy(IFormResponseReader responses, IFormSuggestionRepository suggestions)
{
    public async Task<Result<FormRecordCaptureOriginPreparation?>> ValidateAsync(StructuredRecordCaptureRequest request,
        FormVersionSchema schema, CancellationToken ct)
    {
        if (request.Origin is not { } origin) return Result.Success<FormRecordCaptureOriginPreparation?>(null);
        if (origin.SenderKind is not (StructuredRecordSenderKind.Manual or StructuredRecordSenderKind.Assistant)
            || origin.SenderKind == StructuredRecordSenderKind.Manual && origin.OperatorUserId is null
            || origin.SenderKind == StructuredRecordSenderKind.Manual && origin.OperatorUserId == Guid.Empty
            || origin.SenderKind == StructuredRecordSenderKind.Assistant && origin.OperatorUserId is not null
            || origin.Reason is { } reason && !Text(reason, 1000)
            || origin.Proposal is { } proposal && (!Valid(proposal) || origin.Reason is null)
            || origin.ProposedValues.Count > 40 || origin.IncludedFieldIds.Count > 40
            || origin.ProposedValues.Select(value => value.FieldId).Distinct().Count() != origin.ProposedValues.Count
            || origin.IncludedFieldIds.Distinct().Count() != origin.IncludedFieldIds.Count
            || origin.IncludedFieldIds.Any(id => !origin.ProposedValues.Any(value => value.FieldId == id)))
            return FormRecordErrors.InvalidResponse;

        var questions = schema.Questions.Select(Project).ToArray();
        var canonical = new List<StructuredRecordProposedValue>();
        var changed = new List<Guid>();
        var choices = new List<FormRecordProposedChoice>();
        foreach (var value in origin.ProposedValues)
        {
            var question = questions.SingleOrDefault(question => question.FieldId == value.FieldId);
            if (question is null || question.Type != value.Type) return FormRecordErrors.InvalidResponse;
            var normalized = FormScalarPrefillPolicy.Normalize(question, value.Value, request.Catalogs ?? []);
            if (normalized.IsFailure) return normalized.Error;
            canonical.Add(value with { Value = normalized.Value });
            if (origin.IncludedFieldIds.Contains(value.FieldId))
            {
                if (!request.Answers.TryGetValue(question.Key, out var answer) || answer.Length == 0) changed.Add(value.FieldId);
                else
                {
                    var final = FormScalarPrefillPolicy.Normalize(question, answer, request.Catalogs ?? []);
                    if (final.IsFailure || final.Value != normalized.Value) changed.Add(value.FieldId);
                }
            }
            if (question.SourceCatalogId is { } catalogId)
            {
                var offer = request.Catalogs!.Single(offer => offer.FieldId == value.FieldId && offer.CatalogId == catalogId);
                choices.Add(new(offer, offer.Options.Single(option => option.Id == Guid.ParseExact(normalized.Value, "D"))));
            }
        }
        var captured = origin with { Reason = origin.Reason?.Trim(), ProposedValues = canonical };
        if (origin.Suggestion is { } lineage)
        {
            if (!await ValidLineageAsync(request, captured, lineage, questions, ct)) return FormRecordErrors.InvalidResponse;
        }
        else if (origin.SenderKind == StructuredRecordSenderKind.Assistant && origin.Proposal is null)
            return FormRecordErrors.InvalidResponse;
        return new FormRecordCaptureOriginPreparation(captured, changed, choices);
    }

    private async Task<bool> ValidLineageAsync(StructuredRecordCaptureRequest request, StructuredRecordCaptureOrigin origin,
        StructuredRecordSuggestionOrigin lineage, IReadOnlyList<StructuredFormQuestion> questions, CancellationToken ct)
    {
        if (lineage.OriginRecordId == Guid.Empty || lineage.OriginSuggestionId == Guid.Empty || lineage.SuggestionSetId == Guid.Empty
            || lineage.Mappings.Count > 40 || lineage.Mappings.Select(mapping => mapping.DestinationFieldId).Distinct().Count() != lineage.Mappings.Count)
            return false;
        var source = await responses.FindRecordAsync(lineage.OriginRecordId, ct);
        if (source is null || source.Actor.UserId != request.ActorUserId) return false;
        var set = await suggestions.GetAsync(lineage.SuggestionSetId, ct);
        if (set is null || set.DefinitionId != source.Record.DefinitionId) return false;
        var suggestion = FormDraftPolicy.Read<FormSuggestion[]>(set.SuggestionsJson).SingleOrDefault(value => value.Id == lineage.OriginSuggestionId);
        if (suggestion is null || !suggestion.IsActive || suggestion.DestinationDefinitionId != request.DefinitionId
            || suggestion.Mode == "Assistant" && origin.Proposal is null
            || suggestion.Mode == "Always" && origin.Proposal is not null
            || !FormRecordConditionEvaluator.Matches(suggestion.Conditions, new(source, set.OriginalCulture))) return false;
        var mapped = FormSuggestionMappingPolicy.Map(suggestion, source.Record, questions, request.Catalogs ?? []);
        foreach (var mapping in lineage.Mappings)
        {
            if (!suggestion.Mappings.Any(configured => configured.Source.Kind == mapping.SourceKind
                && configured.Source.FieldId == mapping.SourceFieldId && configured.DestinationFieldId == mapping.DestinationFieldId)) return false;
            var question = questions.SingleOrDefault(question => question.FieldId == mapping.DestinationFieldId);
            var proposed = origin.ProposedValues.SingleOrDefault(value => value.FieldId == mapping.DestinationFieldId);
            if (question is null || proposed is null || !mapped.TryGetValue(question.Key, out var value) || value != proposed.Value) return false;
        }
        return true;
    }

    private static StructuredFormQuestion Project(CanonicalFormField field) => new(field.FieldId, field.Key, field.Type,
        field.Label, field.Help, field.Required, field.MaxLength,
        field.Options?.Select(option => new StructuredFormChoice(option.ChoiceId, option.Label)).ToArray(), field.Source?.Catalog);
    private static bool Valid(StructuredRecordAssistantProposal proposal) => proposal.ProposalId != Guid.Empty
        && Text(proposal.Provenance.ChannelId, 100) && Text(proposal.CapabilityId, 100) && proposal.CapabilityVersion > 0
        && Text(proposal.Provenance.ModelProfileId, 100) && proposal.Provenance.ModelProfileVersion > 0
        && Text(proposal.Provenance.InstructionsId, 100) && proposal.Provenance.InstructionsVersion > 0
        && proposal.Provenance.OccurredAtUtc.Kind == DateTimeKind.Utc;
    private static bool Text(string value, int maximum) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= maximum && StorableText.IsStorable(value);
}
