using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
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
        var query = dbContext.Set<BusinessFormRecord>().AsNoTracking().Where(record => record.DefinitionId == definition.Id);
        if (request.UpToNumber is { } maximum) query = query.Where(record => record.Number <= maximum);
        if (request.VersionIds.Count != 0) query = query.Where(record => request.VersionIds.Contains(record.VersionId));
        if (request.ActorIds.Count != 0) query = query.Where(record => request.ActorIds.Contains(record.ActorUserId));
        if (request.ReceivedFromUtc is { } from) query = query.Where(record => record.SubmittedAtUtc >= from);
        if (request.ReceivedToUtc is { } to) query = query.Where(record => record.SubmittedAtUtc <= to);
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
            var versions = definition.Versions.Where(version => Schema(version).Questions.Any(question => question.FieldId == fieldId)).Select(version => version.Id).ToArray();
            var values = dbContext.Set<BusinessFormRecordValue>().Where(value => value.FieldId == fieldId);
            var files = dbContext.Set<BusinessFormRecordFile>().Where(value => value.FieldId == fieldId);
            switch (condition.Operator)
            {
                case "notAsked": query = query.Where(record => !versions.Contains(record.VersionId)); continue;
                case "unanswered": query = query.Where(record => versions.Contains(record.VersionId) && !values.Any(value => value.RecordId == record.Id) && !files.Any(value => value.RecordId == record.Id)); continue;
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
            else if (field.Type == "choice")
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
        Expression<Func<BusinessFormRecord, object?>> result = record => dbContext.Set<BusinessFormRecordReference>()
            .Where(value => value.RecordId == record.Id && value.FieldId == fieldId).Select(value => value.Text).FirstOrDefault();
        foreach (var version in definition.Versions)
        foreach (var option in Schema(version).Questions.Where(field => field.FieldId == fieldId).SelectMany(field => field.Options ?? []))
        {
            var versionId = version.Id;
            var optionId = option.ChoiceId;
            var label = option.Label[culture];
            Expression<Func<BusinessFormRecord, bool>> matches = record => record.VersionId == versionId && dbContext.Set<BusinessFormRecordValue>()
                .Any(value => value.RecordId == record.Id && value.FieldId == fieldId && value.ChoiceId == optionId);
            var test = new ParameterReplacement(matches.Parameters[0], result.Parameters[0]).Visit(matches.Body);
            result = Expression.Lambda<Func<BusinessFormRecord, object?>>(Expression.Condition(test!, Expression.Convert(Expression.Constant(label), typeof(object)), result.Body), result.Parameters);
        }
        return result;
    }
    private sealed class ParameterReplacement(ParameterExpression source, ParameterExpression target) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == source ? target : base.VisitParameter(node);
    }
    private static FormVersionSchema Schema(FormResponseVersionRow version) => JsonSerializer.Deserialize<FormVersionSchema>(version.SchemaJson, JsonSerializerOptions.Web)!;
    private static IEnumerable<CanonicalFormField> Questions(FormResponseDefinitionRow definition) => definition.Versions.SelectMany(version => Schema(version).Questions);
}
