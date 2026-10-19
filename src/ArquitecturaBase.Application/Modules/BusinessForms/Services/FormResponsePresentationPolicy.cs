using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal static class FormResponsePresentationPolicy
{
    public static IReadOnlyList<FormResponseColumn> Columns(FormResponseDefinitionRow definition, string culture)
    {
        var versions = definition.Versions.OrderByDescending(version => version.Id == definition.ActiveVersionId)
            .ThenByDescending(version => version.Number).ToArray();
        var questions = versions.Select(version => (version.Number, Schema: definition.Schemas[version.Id])).ToArray();
        return questions.SelectMany(version => version.Schema.Questions).DistinctBy(question => question.FieldId)
            .Select(question => new FormResponseColumn(question.FieldId, question.Type, FormRecordPresentationPolicy.Text(question.Label, culture),
                questions.Where(version => version.Schema.Questions.Any(field => field.FieldId == question.FieldId)).Min(version => version.Number),
                questions.SelectMany(version => version.Schema.Questions).Where(field => field.FieldId == question.FieldId)
                    .SelectMany(field => field.Options ?? []).DistinctBy(option => option.ChoiceId)
                    .Select(option => new FormResponseOption(option.ChoiceId, FormRecordPresentationPolicy.Text(option.Label, culture))).ToArray())).ToArray();
    }

    public static IReadOnlyList<FormResponseCell> Cells(IReadOnlyList<FormResponseColumn> columns, StructuredRecordReadReply reply)
    {
        var values = reply.Values.ToDictionary(value => value.FieldId);
        return columns.Select(column => !values.TryGetValue(column.FieldId, out var value)
            ? new FormResponseCell(column.FieldId, column.Type, "NotAsked", null)
            : new FormResponseCell(column.FieldId, column.Type,
                value.Value is null || value.Value is IReadOnlyCollection<StructuredRecordFileValue> { Count: 0 }
                    ? "Unanswered" : "Answered", value.Value)).ToArray();
    }
}
