using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormSuggestionCandidatePolicy(IFormResponseReader responses, IFormSuggestionReader suggestions,
    IFormSelectionReader selections, FormAssistantCandidatePolicy candidates)
{
    public async Task<Result<StructuredFormSuggestionsResponse>> ListAsync(Guid recipientUserId, string culture, Guid sourceRecordId, CancellationToken ct)
    {
        var context = await LoadAsync(recipientUserId, culture, sourceRecordId, ct);
        return context.IsSuccess ? context.Value.Response : context.Error;
    }

    public async Task<Result<Context>> LoadAsync(Guid recipient, string culture, Guid sourceRecordId, CancellationToken ct)
    {
        if (sourceRecordId == Guid.Empty || !await candidates.IsEligibleAsync(recipient, culture, ct)) return FormRecordErrors.AuthorizationChanged;
        var source = await responses.FindRecordAsync(sourceRecordId, ct);
        if (source is null || source.Actor.UserId != recipient) return FormRecordErrors.AuthorizationChanged;
        var set = await suggestions.FindCurrentAsync(source.Record.DefinitionId, ct);
        IReadOnlyList<FormSuggestion> configured = set is null ? [] : FormDraftPolicy.Read<FormSuggestion[]>(set.SuggestionsJson);
        var result = new List<StructuredFormSuggestionCandidate>();
        var preparedCandidates = new Dictionary<Guid, FormPreparedAssistantCandidate>();
        var sourcePresentation = FormRecordPresentationPolicy.Project(source.Record, culture, FormDraftPolicy.Read<FormVersionSchema>(source.Record.SchemaJson));
        if (set is not null)
        {
            var published = await suggestions.FindOptionsAsync(source.Record.DefinitionId, ct);
            var applicable = configured.Where(suggestion => suggestion.IsActive
                && FormRecordConditionEvaluator.Matches(suggestion.Conditions, new(source, set.OriginalCulture))
                && published?.Destinations.Any(target => target.DefinitionId == suggestion.DestinationDefinitionId) == true).ToArray();
            var ids = applicable.Select(suggestion => suggestion.DestinationDefinitionId).Distinct().ToArray();
            var targets = await selections.ListSuggestionTargetsAsync(ids, ct);
            var ancestors = await responses.ListAncestorDefinitionIdsAsync(sourceRecordId, ct);
            foreach (var suggestion in applicable)
            {
                var row = targets.SingleOrDefault(row => row.DefinitionId == suggestion.DestinationDefinitionId);
                if (row is null) continue;
                var prepared = await candidates.PrepareAsync(row, recipient, culture, ct);
                if (prepared.IsFailure) continue;
                var current = prepared.Value;
                preparedCandidates.Add(suggestion.Id, current);
                var presentationCulture = current.Culture;
                var mapped = FormSuggestionMappingPolicy.Map(suggestion, source.Record, current.Questions, current.Catalogs);
                result.Add(new(suggestion.Id, suggestion.Mode, suggestion.When, current.Form, current.DefinitionRevision, current.OriginalCulture,
                    current.Questions, current.Catalogs, Fields(current, mapped, presentationCulture), ancestors.Contains(current.Form.DefinitionId)));
            }
        }
        if (!await candidates.IsEligibleAsync(recipient, culture, ct)) return FormRecordErrors.AuthorizationChanged;
        var final = await suggestions.FindCurrentAsync(source.Record.DefinitionId, ct);
        if (final?.Id != set?.Id) return FormSuggestionErrors.RevisionConflict;
        return new Context(source, configured, preparedCandidates, new(sourceRecordId, set?.Id, set?.Revision ?? 0, sourcePresentation.Name,
            sourcePresentation.Number, sourcePresentation.Values.Where(value => FormSuggestionPolicy.IsScalar(value.Type)).ToArray(), result));
    }

    internal static List<StructuredFormPrefillField> Fields(FormPreparedAssistantCandidate form,
        IReadOnlyDictionary<string, string> values, string culture)
    {
        var fields = new List<StructuredFormPrefillField>();
        foreach (var question in form.Questions)
        {
            if (!values.TryGetValue(question.Key, out var value)) continue;
            var display = value;
            if (question.Type == "choice")
            {
                var id = Guid.ParseExact(value, "D");
                var catalog = form.Catalogs.SingleOrDefault(catalog => catalog.FieldId == question.FieldId);
                if (catalog is not null)
                {
                    var option = catalog.Options.Single(option => option.Id == id);
                    display = option.Translations is { } texts && texts.TryGetValue(culture, out var text) ? text.Text : option.Text;
                }
                else display = FormAssistantCandidatePolicy.Text(question.Options!.Single(option => option.ChoiceId == id).Label, culture, form.OriginalCulture);
            }
            fields.Add(new(question.FieldId, question.Key, question.Type,
                FormAssistantCandidatePolicy.Text(question.Label, culture, form.OriginalCulture), value, display));
        }
        return fields;
    }

    internal sealed record Context(FormResponseStoredRow Source, IReadOnlyList<FormSuggestion> Suggestions, IReadOnlyDictionary<Guid, FormPreparedAssistantCandidate> Prepared, StructuredFormSuggestionsResponse Response);
}
