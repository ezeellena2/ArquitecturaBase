using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal static class FormSuggestionMappingPolicy
{
    public static IReadOnlyDictionary<string,string> Map(FormSuggestion suggestion, FormRecordStoredRow source,
        IReadOnlyList<StructuredFormQuestion> destinationQuestions, IReadOnlyList<StructuredFormCatalogOffer> catalogs)
    {
        var sourceSchema = FormDraftPolicy.Read<FormVersionSchema>(source.SchemaJson);
        using var values = JsonDocument.Parse(source.ValuesJson);
        var answers = values.RootElement.EnumerateArray().ToDictionary(value => value.GetProperty("fieldId").GetGuid(), value => value.GetProperty("value"));
        var result = new Dictionary<string,string>(StringComparer.Ordinal);
        foreach (var mapping in suggestion.Mappings)
        {
            var target = destinationQuestions.SingleOrDefault(question => question.FieldId == mapping.DestinationFieldId);
            if (target is null || !FormSuggestionPolicy.IsScalar(target.Type)) continue;
            string? raw = null;
            if (mapping.Source.Kind == "RecordNumber" && mapping.Source.FieldId is null
                && target.Type is "shortText" or "longText")
                raw = source.Prefix + "-" + source.Number.ToString("D6",CultureInfo.InvariantCulture);
            else if (mapping.Source.Kind == "Field" && mapping.Source.FieldId is { } sourceFieldId)
            {
                var field = sourceSchema.Questions.SingleOrDefault(question => question.FieldId == sourceFieldId);
                if (field is null || field.Type != target.Type || !answers.TryGetValue(sourceFieldId,out var answer)) continue;
                raw = field.Type switch
                {
                    "shortText" or "longText" or "date" or "choice" => answer.GetString(),
                    "decimal" => answer.GetRawText(),
                    "boolean" => answer.GetBoolean() ? "true" : "false",
                    _ => null,
                };
            }
            if (raw is null) continue;
            var value = FormScalarPrefillPolicy.Normalize(target,raw,catalogs);
            if (value.IsSuccess) result.Add(target.Key,value.Value);
        }
        return result;
    }
}
