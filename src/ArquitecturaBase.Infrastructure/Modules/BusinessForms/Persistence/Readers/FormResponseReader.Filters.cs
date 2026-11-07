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
        // A replaced version is never listed: its correction stands for it. A voided response only when it is asked for.
        var query = dbContext.Set<BusinessFormRecord>().AsNoTracking()
            .Where(record => record.DefinitionId == definition.Id && versionIds.Contains(record.VersionId) && record.ReplacedByRecordId == null);
        if (request.State is { } state)
        {
            var selected = Enum.Parse<FormRecordState>(state);
            query = query.Where(record => record.State == selected);
        }
        else if (!request.IncludeVoided) query = query.Where(record => record.State != FormRecordState.Voided);
        if (request.EnteredInApp is { } enteredInApp) query = query.Where(record => record.EnteredInApp == enteredInApp);
        if (request.UpToNumber is { } maximum) query = query.Where(record => record.Number <= maximum);
        if (request.VersionIds.Count != 0) query = query.Where(record => request.VersionIds.Contains(record.VersionId));
        if (request.ActorIds.Count != 0) query = query.Where(record => request.ActorIds.Contains(record.ActorUserId));
        if (request.OwnerUserId is { } owner) query = query.Where(record => record.ActorUserId == owner);
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
            // Presence looks at the whole response, also for a question of the block: some item, none, or not asked.
            switch (condition.Operator)
            {
                case "notAsked": query = query.Where(record => !versions.Contains(record.VersionId)); continue;
                case "unanswered": query = query.Where(record => !values.Any(value => value.RecordId == record.Id) && !files.Any(value => value.RecordId == record.Id)); continue;
                case "answered": query = query.Where(record => values.Any(value => value.RecordId == record.Id) || files.Any(value => value.RecordId == record.Id)); continue;
            }
            if (field.Repeated == true) continue;
            var matching = Matching(field, condition);
            query = query.Where(record => matching.Any(value => value.RecordId == record.Id));
        }
        if (SameItem(ItemConditions(definition, request, ignoredField)) is { } item) query = query.Where(record => item.Any(value => value.RecordId == record.Id));
        return query;
    }
    /// <summary>
    /// The values of the conditions on questions of the block of rows, apart from the ignored one: they are met by one same item
    /// (RG5-A), so each one is looked up in the row of the others.
    /// </summary>
    private IQueryable<BusinessFormRecordValue>[] ItemConditions(FormResponseDefinitionRow definition, ListFormRecordsRequest request, Guid? ignoredField) =>
        request.Conditions.Where(condition => condition.FieldId != ignoredField && condition.Operator is not ("answered" or "unanswered" or "notAsked"))
            .Select(condition => (Condition: condition, Field: Questions(definition).First(question => question.FieldId == condition.FieldId)))
            .Where(item => item.Field.Repeated == true).Select(item => Matching(item.Field, item.Condition)).ToArray();
    /// <summary>The values of the first condition whose item, by its row, also has values for every other one; null without conditions.</summary>
    private static IQueryable<BusinessFormRecordValue>? SameItem(IQueryable<BusinessFormRecordValue>[] conditions)
    {
        if (conditions.Length == 0) return null;
        var item = conditions[0];
        foreach (var other in conditions.Skip(1))
            item = item.Where(value => other.Any(same => same.RecordId == value.RecordId && same.Row == value.Row));
        return item;
    }
    /// <summary>The values of a question that meet a condition with a value, by the operator of its type.</summary>
    private IQueryable<BusinessFormRecordValue> Matching(CanonicalFormField field, FormRecordCondition condition)
    {
        var fieldId = field.FieldId;
        var values = dbContext.Set<BusinessFormRecordValue>().Where(value => value.FieldId == fieldId);
        var text = condition.Values![0];
        switch (field.Type)
        {
            case "shortText" or "longText" when condition.Operator == "equals":
                return values.Where(value => value.TextValue == text);
            case "shortText" or "longText":
                var pattern = LikePatterns.Contains(text);
                return values.Where(value => value.TextValue != null && EF.Functions.ILike(value.TextValue, pattern, LikePatterns.EscapeCharacter));
            case "decimal":
                var number = decimal.Parse(text, CultureInfo.InvariantCulture);
                return condition.Operator switch
                {
                    "gte" => values.Where(value => value.DecimalValue >= number),
                    "lte" => values.Where(value => value.DecimalValue <= number),
                    _ => values.Where(value => value.DecimalValue == number),
                };
            case "date":
                var date = DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                return condition.Operator switch
                {
                    "gte" => values.Where(value => value.DateValue >= date),
                    "lte" => values.Where(value => value.DateValue <= date),
                    _ => values.Where(value => value.DateValue == date),
                };
            case "boolean":
                var answers = condition.Values.Select(bool.Parse).ToArray();
                return values.Where(value => value.BooleanValue != null && answers.Contains(value.BooleanValue.Value));
            case "choice" or "multiChoice":
                var selected = condition.Values.Select(Guid.Parse).ToArray();
                return values.Where(value => value.ChoiceId != null && selected.Contains(value.ChoiceId.Value));
            default:
                throw new InvalidOperationException("The condition was validated for another question type.");
        }
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
