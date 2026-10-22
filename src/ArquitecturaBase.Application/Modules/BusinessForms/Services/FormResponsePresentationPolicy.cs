using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal static class FormResponsePresentationPolicy
{
    public static IReadOnlyList<FormResponseColumn> Columns(FormResponseDefinitionRow definition, string culture)
    {
        var versions = definition.Versions.OrderByDescending(version => version.Id == definition.ActiveVersionId)
            .ThenByDescending(version => version.Number).ToArray();
        var questions = versions.SelectMany(version => definition.Schemas[version.Id].Questions
            .Select(question => (Version: version, Question: question))).ToArray();
        return questions.DistinctBy(item => item.Question.FieldId)
            .Select(item => new FormResponseColumn(item.Question.FieldId, item.Question.Type,
                FormRecordPresentationPolicy.Text(item.Question.Label, item.Version.DisplayCulture(culture)),
                questions.Where(other => other.Question.FieldId == item.Question.FieldId).Min(other => other.Version.Number),
                questions.Where(other => other.Question.FieldId == item.Question.FieldId)
                    .SelectMany(other => (other.Question.Options ?? []).Select(option => (other.Version, Option: option)))
                    .DistinctBy(other => other.Option.ChoiceId)
                    .Select(other => new FormResponseOption(other.Option.ChoiceId,
                        FormRecordPresentationPolicy.Text(other.Option.Label, other.Version.DisplayCulture(culture)))).ToArray(),
                item.Question.Source?.Catalog is Guid catalogId
                    && definition.CatalogValueTypes?.GetValueOrDefault(catalogId) is { } valueType && valueType != "Text"
                        ? valueType : null)).ToArray();
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
