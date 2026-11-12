using System.Linq.Expressions;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
internal sealed partial class FormResponseReader
{
    // The sheet's filter, shared with the dashboard (FormResponseQuery).
    private IQueryable<BusinessFormRecord> Filter(FormResponseDefinitionRow definition, ListFormRecordsRequest request, Guid? ignoredField = null) =>
        FormResponseQuery.Filter(dbContext, definition, request, ignoredField);
    private IQueryable<BusinessFormRecord> Ordered(IQueryable<BusinessFormRecord> query, FormResponseDefinitionRow definition, ListFormRecordsRequest request)
    {
        var sortable = new Dictionary<string, Expression<Func<BusinessFormRecord, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["number"] = record => record.Number,
            ["submittedAtUtc"] = record => record.SubmittedAtUtc,
            ["actor"] = record => dbContext.Users.Where(user => user.Id == record.ActorUserId).Select(user => user.DisplayName).FirstOrDefault(),
        };
        if (request.SortFieldId is { } fieldId)
        {
            var field = FormResponseQuery.Questions(definition).First(question => question.FieldId == fieldId);
            var values = dbContext.Set<BusinessFormRecordValue>().Where(value => value.FieldId == fieldId);
            sortable["field"] = field.Type switch
            {
                "decimal" => record => values.Where(value => value.RecordId == record.Id).Select(value => value.DecimalValue).FirstOrDefault(),
                "date" => record => values.Where(value => value.RecordId == record.Id).Select(value => value.DateValue).FirstOrDefault(),
                "boolean" => record => values.Where(value => value.RecordId == record.Id).Select(value => value.BooleanValue).FirstOrDefault(),
                "photo" or "document" => record => dbContext.Set<BusinessFormRecordFile>().Count(value => value.RecordId == record.Id && value.FieldId == fieldId),
                "choice" => ChoiceSort(definition, fieldId, request.DisplayCulture),
                _ => record => values.Where(value => value.RecordId == record.Id).Select(value => value.TextValue).FirstOrDefault(),
            };
        }
        return query.ApplySort(SortDescriptor.Parse(request.Sort), sortable, new("number", true), record => record.Number);
    }
    private Expression<Func<BusinessFormRecord, object?>> ChoiceSort(FormResponseDefinitionRow definition, Guid fieldId, string culture)
    {
        var field = FormResponseQuery.Questions(definition).First(question => question.FieldId == fieldId);
        if (field.Source?.Catalog is not null)
        {
            var references=FormResponseQuery.ReferenceLabels(dbContext, definition, culture);
            return record=>references.Where(value=>value.RecordId==record.Id&&value.FieldId==fieldId).Select(value=>value.Label).FirstOrDefault();
        }
        var texts = FormResponseQuery.QuestionVersions(definition).Where(item => item.Question.FieldId == fieldId)
            .SelectMany(item => (item.Question.Options ?? []).Select(option => new ChoiceLabelRow
            {
                VersionId = item.Version.Id, ChoiceId = option.ChoiceId,
                Label = Text(option.Label, item.Version.DisplayCulture(culture))
            })).ToArray();
        var labels = dbContext.Database.SqlQueryRaw<ChoiceLabelRow>(
            """SELECT (value->>'versionId')::uuid AS "VersionId", (value->>'choiceId')::uuid AS "ChoiceId", value->>'label' AS "Label" FROM jsonb_array_elements({0}::jsonb) AS value""",
            System.Text.Json.JsonSerializer.Serialize(texts, System.Text.Json.JsonSerializerOptions.Web));
        var values = dbContext.Set<BusinessFormRecordValue>().Where(value => value.FieldId == fieldId);
        return record => labels.Where(label => label.VersionId == record.VersionId
            && values.Any(value => value.RecordId == record.Id && value.ChoiceId == label.ChoiceId)).Select(label => label.Label).FirstOrDefault();
    }
    private sealed class ChoiceLabelRow
    {
        public Guid VersionId { get; init; }
        public Guid ChoiceId { get; init; }
        public string Label { get; init; } = "";
    }
}
