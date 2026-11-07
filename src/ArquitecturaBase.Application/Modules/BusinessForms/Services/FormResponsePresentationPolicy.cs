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
        // A block of rows has one column with its questions inside, joined across the versions that had that block (RG4-A).
        return Columns(definition, culture, questions.Where(item => item.Question.Repeated != true).ToArray(),
            group => questions.Where(item => item.Question.Repeated == true
                && definition.Schemas[item.Version.Id].Questions.Any(question => question.FieldId == group)).ToArray());
    }

    private static FormResponseColumn[] Columns(FormResponseDefinitionRow definition, string culture,
        (FormResponseVersionRow Version, CanonicalFormField Question)[] questions,
        Func<Guid, (FormResponseVersionRow Version, CanonicalFormField Question)[]>? blockQuestions) =>
        questions.DistinctBy(item => item.Question.FieldId)
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
                        ? valueType : null,
                item.Question.Source?.Catalog is Guid fieldsCatalogId
                    && definition.CatalogFields?.GetValueOrDefault(fieldsCatalogId) is { Count: > 0 } catalogFields
                        ? catalogFields.Select(field => new FormResponseCatalogField(field.Id, field.Key,
                            FormRecordPresentationPolicy.Text(field.Name, item.Version.DisplayCulture(culture)), field.Type, field.Role)).ToArray()
                        : null,
                item.Question.Type == "group" && blockQuestions is not null
                    ? Columns(definition, culture, blockQuestions(item.Question.FieldId), null) : null)).ToArray();

    public static IReadOnlyList<FormResponseCell> Cells(IReadOnlyList<FormResponseColumn> columns, StructuredRecordReadReply reply) =>
        Cells(columns, reply.Values);

    private static FormResponseCell[] Cells(IReadOnlyList<FormResponseColumn> columns, IReadOnlyList<StructuredRecordReadValue> read)
    {
        var values = read.ToDictionary(value => value.FieldId);
        return columns.Select(column => !values.TryGetValue(column.FieldId, out var value)
            ? new FormResponseCell(column.FieldId, column.Type, "NotAsked", null)
            // Each item of a block has one cell per question of its column: the ones its version did not have were not asked.
            : value.Value is IReadOnlyList<StructuredRecordReadRow> rows
            ? new FormResponseCell(column.FieldId, column.Type, rows.Count == 0 ? "Unanswered" : "Answered",
                rows.Select(row => new FormResponseRowCells(Cells(column.Questions ?? [], row.Values))).ToArray())
            : new FormResponseCell(column.FieldId, column.Type,
                value.Value is null || value.Value is IReadOnlyCollection<StructuredRecordFileValue> { Count: 0 }
                    || value.Value is IReadOnlyCollection<StructuredRecordChoiceValue> { Count: 0 }
                    ? "Unanswered" : "Answered", value.Value)).ToArray();
    }
}
