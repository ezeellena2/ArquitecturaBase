using System.Globalization;
using System.Linq.Expressions;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
internal sealed partial class FormResponseReader
{
    private IQueryable<BusinessFormRecord> Filter(FormResponseDefinitionRow definition, ListFormRecordsRequest request, Guid? ignoredField = null)
    {
        var versionIds = definition.Versions.Select(version => version.Id).ToArray();
        var query = dbContext.Set<BusinessFormRecord>().AsNoTracking()
            .Where(record => record.DefinitionId == definition.Id && versionIds.Contains(record.VersionId));
        if (request.State is { } state)
        {
            var selected = Enum.Parse<FormRecordState>(state);
            query = query.Where(record => record.State == selected);
        }
        if (request.UpToNumber is { } maximum) query = query.Where(record => record.Number <= maximum);
        if (request.VersionIds.Count != 0) query = query.Where(record => request.VersionIds.Contains(record.VersionId));
        if (request.ActorIds.Count != 0) query = query.Where(record => request.ActorIds.Contains(record.ActorUserId));
        if (request.ReceivedFromUtc is { } from) query = query.Where(record => record.SubmittedAtUtc >= from);
        if (request.ReceivedToUtc is { } to) query = query.Where(record => record.SubmittedAtUtc < to);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = LikePatterns.Contains(request.Search.Trim());
            query = query.Where(record => dbContext.Set<BusinessFormRecordValue>().Any(value => value.RecordId == record.Id && value.Type == "longText"
                && value.TextValue != null && EF.Functions.ILike(value.TextValue, pattern, LikePatterns.EscapeCharacter)));
        }
        foreach (var condition in request.Conditions.Where(condition => condition.FieldId != ignoredField))
        {
            var fieldId = condition.FieldId;
            var field = Questions(definition).First(question => question.FieldId == fieldId);
            var versions = definition.Versions.Where(version => definition.Schemas[version.Id].Questions.Any(question => question.FieldId == fieldId)).Select(version => version.Id).ToArray();
            var values = dbContext.Set<BusinessFormRecordValue>().Where(value => value.FieldId == fieldId);
            var files = dbContext.Set<BusinessFormRecordFile>().Where(value => value.FieldId == fieldId);
            switch (condition.Operator)
            {
                case "notAsked": query = query.Where(record => !versions.Contains(record.VersionId)); continue;
                case "unanswered": query = query.Where(record => !values.Any(value => value.RecordId == record.Id) && !files.Any(value => value.RecordId == record.Id)); continue;
                case "answered": query = query.Where(record => values.Any(value => value.RecordId == record.Id) || files.Any(value => value.RecordId == record.Id)); continue;
            }
            var text = condition.Values![0];
            if (field.Type is "shortText" or "longText")
            {
                if (condition.Operator == "equals") query = query.Where(record => values.Any(value => value.RecordId == record.Id && value.TextValue == text));
                else
                {
                    var pattern = LikePatterns.Contains(text);
                    query = query.Where(record => values.Any(value => value.RecordId == record.Id && value.TextValue != null && EF.Functions.ILike(value.TextValue, pattern, LikePatterns.EscapeCharacter)));
                }
            }
            else if (field.Type == "decimal")
            {
                var number = decimal.Parse(text, CultureInfo.InvariantCulture);
                query = condition.Operator switch
                {
                    "gte" => query.Where(record => values.Any(value => value.RecordId == record.Id && value.DecimalValue >= number)),
                    "lte" => query.Where(record => values.Any(value => value.RecordId == record.Id && value.DecimalValue <= number)),
                    _ => query.Where(record => values.Any(value => value.RecordId == record.Id && value.DecimalValue == number)),
                };
            }
            else if (field.Type == "date")
            {
                var date = DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                query = condition.Operator switch
                {
                    "gte" => query.Where(record => values.Any(value => value.RecordId == record.Id && value.DateValue >= date)),
                    "lte" => query.Where(record => values.Any(value => value.RecordId == record.Id && value.DateValue <= date)),
                    _ => query.Where(record => values.Any(value => value.RecordId == record.Id && value.DateValue == date)),
                };
            }
            else if (field.Type == "boolean")
            {
                var selected = condition.Values.Select(bool.Parse).ToArray();
                query = query.Where(record => values.Any(value => value.RecordId == record.Id && value.BooleanValue != null && selected.Contains(value.BooleanValue.Value)));
            }
            else if (field.Type is "choice" or "multiChoice")
            {
                var selected = condition.Values.Select(Guid.Parse).ToArray();
                query = query.Where(record => values.Any(value => value.RecordId == record.Id && value.ChoiceId != null && selected.Contains(value.ChoiceId.Value)));
            }
        }
        return query;
    }
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
            var field = Questions(definition).First(question => question.FieldId == fieldId);
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
        var field = Questions(definition).First(question => question.FieldId == fieldId);
        if (field.Source?.Catalog is not null)
        {
            var references=ReferenceLabels(definition, culture);
            return record=>references.Where(value=>value.RecordId==record.Id&&value.FieldId==fieldId).Select(value=>value.Label).FirstOrDefault();
        }
        var texts = QuestionVersions(definition).Where(item => item.Question.FieldId == fieldId)
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
    private static IEnumerable<(FormResponseVersionRow Version, CanonicalFormField Question)> QuestionVersions(FormResponseDefinitionRow definition) =>
        definition.Versions.OrderByDescending(version => version.Number).SelectMany(version =>
            definition.Schemas[version.Id].Questions.Select(question => (version, question)));
    private static IEnumerable<CanonicalFormField> Questions(FormResponseDefinitionRow definition) => QuestionVersions(definition).Select(item => item.Question);
}
