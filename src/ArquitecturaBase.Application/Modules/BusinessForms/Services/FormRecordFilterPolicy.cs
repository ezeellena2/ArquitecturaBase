using System.Globalization;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal static class FormRecordFilterPolicy
{
    public static Result Validate(ListFormRecordsRequest request, FormResponseDefinitionRow definition)
    {
        var fields = definition.Versions.SelectMany(version => definition.Schemas[version.Id].Questions)
            .DistinctBy(field => field.FieldId).ToDictionary(field => field.FieldId);
        var sort = SortDescriptor.Parse(request.Sort);
        if (request.VersionIds.Any(id => !definition.Versions.Any(version => version.Id == id))
            || request.ActorIds.Any(id => id == Guid.Empty)
            || request.SortFieldId is { } sortId && !fields.ContainsKey(sortId)
            || sort?.Field.Equals("field", StringComparison.OrdinalIgnoreCase) == true && request.SortFieldId is null
            || sort?.Field.Equals("field", StringComparison.OrdinalIgnoreCase) == true
                && request.SortFieldId is { } selected && fields.TryGetValue(selected, out var sortField) && sortField.Type == "multiChoice"
            || request.ReceivedFromUtc > request.ReceivedToUtc) return FormRecordErrors.FilterInvalid;
        foreach (var condition in request.Conditions)
        {
            if (!fields.TryGetValue(condition.FieldId, out var field)) return FormRecordErrors.FilterInvalid;
            var values = condition.Values ?? [];
            if (condition.Operator is "answered" or "unanswered" or "notAsked")
            {
                if (values.Count != 0) return FormRecordErrors.FilterInvalid;
                continue;
            }
            if (values.Count == 0 || values.Any(string.IsNullOrWhiteSpace)) return FormRecordErrors.FilterInvalid;
            var valid = field.Type switch
            {
                "shortText" or "longText" => condition.Operator is "contains" or "equals" && values.Count == 1 && values[0].Length <= 600,
                "decimal" => condition.Operator is "equals" or "gte" or "lte" && values.Count == 1
                    && decimal.TryParse(values[0], NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _),
                "date" => condition.Operator is "equals" or "gte" or "lte" && values.Count == 1
                    && DateOnly.TryParseExact(values[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                "boolean" => condition.Operator == "in" && values.Count <= 2 && values.All(value => value is "true" or "false"),
                "multiChoice" => condition.Operator == "anyOf" && values.Count <= 200 && values.All(value => Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty),
                "choice" => condition.Operator == "in" && values.Count <= 200 && values.All(value => Guid.TryParse(value, out var id) && id != Guid.Empty),
                _ => false,
            };
            if (!valid) return FormRecordErrors.FilterInvalid;
        }
        return Result.Success();
    }
}
