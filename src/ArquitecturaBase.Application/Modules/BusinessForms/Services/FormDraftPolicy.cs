using System.Collections.ObjectModel;
using System.Text.Json;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal static class FormDraftPolicy
{
    internal static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);
    public static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonOptions) ?? throw new InvalidOperationException("The stored form data is invalid.");
    public static SaveFormDraftRequest Normalize(SaveFormDraftRequest request, bool supplyMissingSubmitLabel = true) => request with
    {
        Name = NormalizeName(request.Name)!, Prefix = request.Prefix.Trim().ToUpperInvariant(),
        Draft = request.Draft with
        {
            SubmitLabel = request.Draft.SubmitLabel is { } submitLabel ? Sort(submitLabel)
                : supplyMissingSubmitLabel ? Sort(new Dictionary<string, string> { [request.Draft.Culture] = request.Draft.Culture == "en" ? "Submit" : "Enviar" }) : null,
            Fields = request.Draft.Fields.Select(field => field with
            {
                Label = Sort(field.Label),
                Help = field.Help is null ? null : SortNullable(field.Help),
                MaxLength = field.MaxLength ?? (field.Type == "shortText" ? 80 : field.Type == "longText" ? 600 : null),
                Options = field.Options?.Select(option => option with { Label = Sort(option.Label) }).ToArray(),
            }).ToArray(),
        },
    };
    public static IReadOnlyDictionary<string, string>? NormalizeName(IReadOnlyDictionary<string, string>? values) => values is null ? null :
        new ReadOnlyDictionary<string, string>(new SortedDictionary<string, string>(values.ToDictionary(pair => pair.Key, pair => pair.Value?.Trim()!), StringComparer.Ordinal));
    private static ReadOnlyDictionary<string, string> Sort(IReadOnlyDictionary<string, string> values) =>
        new ReadOnlyDictionary<string, string>(new SortedDictionary<string, string>(values.ToDictionary(pair => pair.Key, pair => string.IsNullOrWhiteSpace(pair.Value) ? "" : pair.Value), StringComparer.Ordinal));
    private static ReadOnlyDictionary<string, string?> SortNullable(IReadOnlyDictionary<string, string?> values) =>
        new ReadOnlyDictionary<string, string?>(new SortedDictionary<string, string?>(values.ToDictionary(pair => pair.Key, pair => string.IsNullOrWhiteSpace(pair.Value) ? null : pair.Value), StringComparer.Ordinal));
    public static FormDefinitionResponse Definition(BusinessFormDefinition value) => Definition(value.Id, value.NameJson, value.Prefix,
        value.PrefixLocked, value.Revision, value.Culture, value.SubmitLabelJson, value.SchemaJson);
    public static FormDefinitionResponse Definition(Guid id, string nameJson, string prefix, bool prefixLocked, long revision,
        string culture, string submitLabelJson, string schemaJson) => new(id, Read<Dictionary<string, string>>(nameJson), prefix, prefixLocked, revision,
            new(1, culture, Read<Dictionary<string, string>>(submitLabelJson), Read<FormQuestionSchema>(schemaJson).Fields));
    public static Result<FormDraftResolution> Resolve(SaveFormDraftRequest request, BusinessFormDefinition? existing,
        FormRetiredIdentities existingRetiredIdentities)
    {
        var draft = request.Draft;
        var oldFields = existing is null ? Array.Empty<CanonicalFormField>() : Read<FormQuestionSchema>(existing.SchemaJson).Fields.ToArray();
        if (existing is not null && LostTranslations(Read<Dictionary<string, string>>(existing.NameJson), request.Name))
            return FieldErrors.On(FormDefinitionErrors.TranslationsWouldBeLost, "name");
        if (existing is not null && draft.Culture != existing.Culture) return FieldErrors.On(FormDefinitionErrors.ContentInvalid, "draft.culture");
        if (draft.SubmitLabel is not { } submit)
            return FieldErrors.On(FormDefinitionErrors.TranslationsWouldBeLost, "draft.submitLabel");
        if (existing is not null && LostTranslations(Read<Dictionary<string, string>>(existing.SubmitLabelJson), submit))
            return FieldErrors.On(FormDefinitionErrors.TranslationsWouldBeLost, "draft.submitLabel");
        var oldById = oldFields.ToDictionary(x => x.FieldId);
        var usedOld = new HashSet<Guid>(); var currentFieldIds = new HashSet<Guid>(); var currentChoiceIds = new HashSet<Guid>();
        var retiredFields = new HashSet<Guid>(); var retiredChoices = new HashSet<Guid>();
        var existingRetiredFields = existingRetiredIdentities.Fields.ToHashSet();
        var existingRetiredChoices = existingRetiredIdentities.Choices.ToHashSet();
        var fields = new List<CanonicalFormField>(); var fieldMap = new List<FormFieldIdentityMap>(); var choiceMap = new List<FormChoiceIdentityMap>();
        for (var i = 0; i < draft.Fields.Count; i++)
        {
            var input = draft.Fields[i]; var path = $"draft.fields[{i}]";
            CanonicalFormField? prior = null;
            var sourceId = input.FieldId ?? input.ReplacesFieldId;
            if (sourceId.HasValue)
            {
                if (existingRetiredFields.Contains(sourceId.Value)) return FormDraftErrors.IdentityRetired;
                if (!oldById.TryGetValue(sourceId.Value, out prior) || !usedOld.Add(sourceId.Value)) return FieldErrors.On(FormDraftErrors.IdentityInvalid, path + (input.FieldId.HasValue ? ".fieldId" : ".replacesFieldId"));
                if (input.FieldId.HasValue && input.Type != prior.Type)
                    return FieldErrors.On(FormDraftErrors.QuestionConfigurationInvalid, path + ".type");
                if (input.FieldId.HasValue && input.Source != prior.Source)
                    return FieldErrors.On(FormDraftErrors.QuestionConfigurationInvalid, path + ".source.catalog");
                if (input.ReplacesFieldId.HasValue && input.Type == prior.Type && input.Source == prior.Source)
                    return FieldErrors.On(FormDraftErrors.QuestionConfigurationInvalid, path + ".type");
                if (LostTranslations(prior.Label, input.Label)) return FieldErrors.On(FormDefinitionErrors.TranslationsWouldBeLost, path + ".label");
                if (prior.Help is not null && prior.Help.Keys.Any(key => input.Help is null || !input.Help.ContainsKey(key)))
                    return FieldErrors.On(FormDefinitionErrors.TranslationsWouldBeLost, path + ".help");
            }
            var fieldId = input.FieldId ?? FormQuestionIdentity.Create(TypeOf(input.Type)).Value.FieldId;
            currentFieldIds.Add(fieldId);
            var key = input.FieldId.HasValue ? prior!.Key : "field_" + fieldId.ToString("N");
            List<CanonicalFormChoice>? options = input.Type == "choice" && input.Source is null ? [] : null;
            if (options is not null)
            {
                var previousOptions = input.FieldId.HasValue ? (prior!.Options ?? []).ToDictionary(x => x.ChoiceId) : [];
                for (var j = 0; j < input.Options!.Count; j++)
                {
                    var option = input.Options[j]; var optionPath = path + $".options[{j}]";
                    if (option.ChoiceId is { } oldId)
                    {
                        if (existingRetiredChoices.Contains(oldId)) return FormDraftErrors.IdentityRetired;
                        if (!previousOptions.TryGetValue(oldId, out var oldOption) || currentChoiceIds.Contains(oldId))
                            return FieldErrors.On(FormDraftErrors.IdentityInvalid, optionPath + ".choiceId");
                        if (LostTranslations(oldOption.Label, option.Label)) return FieldErrors.On(FormDefinitionErrors.TranslationsWouldBeLost, optionPath + ".label");
                    }
                    var choiceId = option.ChoiceId ?? FormChoiceIdentity.Create(fieldId).Value.ChoiceId;
                    currentChoiceIds.Add(choiceId); options.Add(new(choiceId, option.Label));
                    choiceMap.Add(new(input.LocalId, option.LocalId, fieldId, choiceId));
                }
            }
            var help = input.Help?.Where(pair => !string.IsNullOrWhiteSpace(pair.Value)).ToDictionary(pair => pair.Key, pair => pair.Value!, StringComparer.Ordinal);
            fields.Add(new(fieldId, key, input.Type, input.Label, help is { Count: > 0 } ? help : null, input.Required, input.MaxLength,
                options?.AsReadOnly(), input.Source, input.Files));
            fieldMap.Add(new(input.LocalId, fieldId, key, input.ReplacesFieldId));
        }
        retiredFields.UnionWith(oldFields.Where(field => !currentFieldIds.Contains(field.FieldId)).Select(field => field.FieldId));
        retiredChoices.UnionWith(oldFields.SelectMany(field => field.Options ?? []).Where(option => !currentChoiceIds.Contains(option.ChoiceId)).Select(option => option.ChoiceId));
        return new FormDraftResolution(new(1, draft.Culture, submit, fields.AsReadOnly()), new(fieldMap.AsReadOnly(), choiceMap.AsReadOnly()),
            new(retiredFields.Except(existingRetiredFields).Order().ToArray(), retiredChoices.Except(existingRetiredChoices).Order().ToArray()));
    }
    private static bool LostTranslations(IReadOnlyDictionary<string, string> previous, IReadOnlyDictionary<string, string> current) => previous.Any(pair => !string.IsNullOrWhiteSpace(pair.Value) && !current.ContainsKey(pair.Key));
    private static FormQuestionType TypeOf(string type) => type switch
    {
        "shortText" => FormQuestionType.ShortText, "longText" => FormQuestionType.LongText, "decimal" => FormQuestionType.Number,
        "photo" => FormQuestionType.Photo, "document" => FormQuestionType.Document,
        "date" => FormQuestionType.Date, "boolean" => FormQuestionType.Boolean, "choice" => FormQuestionType.Choice,
        _ => throw new InvalidOperationException("The validated field type is unsupported."),
    };
}
