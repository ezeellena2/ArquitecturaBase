using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal static class FormSuggestionPolicy
{
    public static Result<IReadOnlyList<FormSuggestion>> Normalize(SaveFormSuggestionsRequest request,
        FormSuggestionOptionsResponse options)
    {
        if (request.ExpectedRevision is null or < 0 or >= 9007199254740991) return Invalid("expectedRevision");
        if (request.Suggestions is not { Count: <= 20 }) return Invalid("suggestions");
        var ids = new HashSet<Guid>();
        var normalized = new List<FormSuggestion>();
        var source = options.Source;
        var filterDefinition = new FormResponseDefinitionRow(source.DefinitionId, "", source.VersionId,
            [new(source.VersionId, 1, "{}", FormDraftPolicy.Serialize(new FormVersionSchema(1, source.Questions)))]);
        foreach (var (suggestion, index) in request.Suggestions.Select((value,index) => (value,index)))
        {
            var path = $"suggestions[{index}]";
            if (suggestion is null || suggestion.Id == Guid.Empty || !ids.Add(suggestion.Id)) return Invalid(path + ".id");
            var destination = options.Destinations.SingleOrDefault(form => form.DefinitionId == suggestion.DestinationDefinitionId);
            if (destination is null || destination.DefinitionId == source.DefinitionId) return Invalid(path + ".destinationDefinitionId");
            if (suggestion.Mode is not ("Assistant" or "Always")) return Invalid(path + ".mode");
            var when = suggestion.Mode == "Always" || string.IsNullOrWhiteSpace(suggestion.When) ? null : suggestion.When.Trim();
            if (suggestion.Mode == "Assistant" && when is null) return Invalid(path + ".when");
            if (when is not null && (when.Length > 1000 || !StorableText.IsStorable(when))) return Invalid(path + ".when");
            if (suggestion.Conditions is not { Count: <= 10 } || suggestion.Conditions.Any(condition => condition is null)) return Invalid(path + ".conditions");
            var conditions = suggestion.Conditions.Select(condition => condition!).ToArray();
            if (conditions.Any(condition => condition.Values is { Count: > 200 }
                || condition.Values?.Any(value => value is null || value.Length > 600 || !StorableText.IsStorable(value)) == true)
                || FormRecordFilterPolicy.Validate(new() { Conditions = conditions }, filterDefinition).IsFailure) return Invalid(path + ".conditions");
            if (suggestion.Mappings is not { Count: <= 40 }) return Invalid(path + ".mappings");
            var targetIds = new HashSet<Guid>();
            var mappings = new List<FormSuggestionMapping>();
            foreach (var (mapping, mappingIndex) in suggestion.Mappings.Select((value,index) => (value,index)))
            {
                var mappingPath = path + $".mappings[{mappingIndex}]";
                if (mapping?.Source is null) return Invalid(mappingPath + ".source");
                var target = destination.Questions.SingleOrDefault(field => field.FieldId == mapping.DestinationFieldId);
                if (target is null || !targetIds.Add(target.FieldId) || !IsScalar(target.Type)) return Invalid(mappingPath + ".destinationFieldId");
                if (mapping.Source.Kind == "RecordNumber")
                {
                    if (mapping.Source.FieldId is not null || target.Type is not ("shortText" or "longText")) return Invalid(mappingPath + ".source");
                }
                else if (mapping.Source.Kind == "Field")
                {
                    var field = source.Questions.SingleOrDefault(field => field.FieldId == mapping.Source.FieldId);
                    if (field is null || !CanMap(field.Type, field.Source?.Catalog, target.Type, target.Source?.Catalog)) return Invalid(mappingPath + ".source");
                }
                else return Invalid(mappingPath + ".source.kind");
                mappings.Add(new(new(mapping.Source.Kind, mapping.Source.FieldId), mapping.DestinationFieldId));
            }
            normalized.Add(new(suggestion.Id, destination.DefinitionId, suggestion.Mode, when, suggestion.IsActive,
                conditions.Select(condition => condition with { Values = condition.Values?.ToArray() }).ToArray(), mappings.ToArray())
                { DestinationVersionId = destination.VersionId });
        }
        return normalized.ToArray();
    }
    internal static bool CanMap(string sourceType, Guid? sourceCatalog, string targetType, Guid? targetCatalog) =>
        IsScalar(sourceType) && sourceType == targetType
        && (sourceType != "choice" || sourceCatalog is { } catalog && catalog != Guid.Empty && catalog == targetCatalog);
    internal static bool IsScalar(string type) => type is "shortText" or "longText" or "decimal" or "date" or "boolean" or "choice";
    private static ValidationError Invalid(string path) => FieldErrors.On(FormSuggestionErrors.ContentInvalid, path);
}
