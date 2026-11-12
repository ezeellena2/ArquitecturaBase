using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
/// <summary>
/// What the sheet, its counts, its navigation, its Excel and the dashboard (DOC7-A) share: the responses a filter takes, with its
/// grammar and the restriction to one's own responses (<c>OwnerUserId</c>), and the labels of the catalog options they captured.
/// </summary>
internal static class FormResponseQuery
{
    public static IQueryable<BusinessFormRecord> Filter(ApplicationDbContext dbContext, FormResponseDefinitionRow definition, ListFormRecordsRequest request,
        Guid? ignoredField = null)
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
            var matching = Matching(dbContext, field, condition);
            query = query.Where(record => matching.Any(value => value.RecordId == record.Id));
        }
        if (SameItem(ItemConditions(dbContext, definition, request, ignoredField)) is { } item) query = query.Where(record => item.Any(value => value.RecordId == record.Id));
        return query;
    }

    /// <summary>
    /// The values of the conditions on questions of the block of rows, apart from the ignored one: they are met by one same item
    /// (RG5-A), so each one is looked up in the row of the others.
    /// </summary>
    public static IQueryable<BusinessFormRecordValue>[] ItemConditions(ApplicationDbContext dbContext, FormResponseDefinitionRow definition,
        ListFormRecordsRequest request, Guid? ignoredField) =>
        request.Conditions.Where(condition => condition.FieldId != ignoredField && condition.Operator is not ("answered" or "unanswered" or "notAsked"))
            .Select(condition => (Condition: condition, Field: Questions(definition).First(question => question.FieldId == condition.FieldId)))
            .Where(item => item.Field.Repeated == true).Select(item => Matching(dbContext, item.Field, item.Condition)).ToArray();

    /// <summary>The values of the first condition whose item, by its row, also has values for every other one; null without conditions.</summary>
    public static IQueryable<BusinessFormRecordValue>? SameItem(IQueryable<BusinessFormRecordValue>[] conditions)
    {
        if (conditions.Length == 0) return null;
        var item = conditions[0];
        foreach (var other in conditions.Skip(1))
            item = item.Where(value => other.Any(same => same.RecordId == value.RecordId && same.Row == value.Row));
        return item;
    }

    public static IEnumerable<(FormResponseVersionRow Version, CanonicalFormField Question)> QuestionVersions(FormResponseDefinitionRow definition) =>
        definition.Versions.OrderByDescending(version => version.Number).SelectMany(version =>
            definition.Schemas[version.Id].Questions.Select(question => (version, question)));

    public static IEnumerable<CanonicalFormField> Questions(FormResponseDefinitionRow definition) => QuestionVersions(definition).Select(item => item.Question);

    public static IQueryable<ReferenceLabelRow> ReferenceLabels(ApplicationDbContext dbContext, FormResponseDefinitionRow definition, string culture) =>
        dbContext.Database.SqlQueryRaw<ReferenceLabelRow>(ReferenceLabelSql("{0}"), Presentations(definition, culture));

    /// <summary>
    /// The label of a captured option in the version's culture, then in the other one (a captured option lacks a translation only in
    /// its catalog's original language, as FormRecordReferencePolicy.Text reads it), then the offered text of legacy references; the
    /// dashboard (DOC7-A) embeds it to break ties by label. <paramref name="presentations"/> is where <see cref="Presentations"/> goes.
    /// </summary>
    public static string ReferenceLabelSql(string presentations) => $"""
        SELECT reference."Id", reference."DefinitionId", reference."RecordId", reference."FieldId", reference."CatalogId", reference."ItemId",
            COALESCE(reference."TranslationsJson" -> (presentation.value->>'culture') ->> 'text',
                reference."TranslationsJson" -> (CASE presentation.value->>'culture' WHEN 'en' THEN 'es' ELSE 'en' END) ->> 'text',
                reference."Text") AS "Label"
        FROM "BusinessFormRecordReferences" reference
        JOIN "BusinessFormRecords" record ON record."Id" = reference."RecordId"
        JOIN jsonb_array_elements({presentations}::jsonb) presentation(value) ON (presentation.value->>'versionId')::uuid = record."VersionId"
        """;

    /// <summary>The culture each version of the form shows for the request's one, as <see cref="ReferenceLabelSql"/> reads it.</summary>
    public static string Presentations(FormResponseDefinitionRow definition, string culture) =>
        JsonSerializer.Serialize(definition.Versions.Select(version => new { versionId = version.Id, culture = version.DisplayCulture(culture) }),
            JsonSerializerOptions.Web);

    /// <summary>The values of a question that meet a condition with a value, by the operator of its type.</summary>
    private static IQueryable<BusinessFormRecordValue> Matching(ApplicationDbContext dbContext, CanonicalFormField field, FormRecordCondition condition)
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

    public sealed class ReferenceLabelRow
    {
        public Guid Id { get; init; }
        public Guid DefinitionId { get; init; }
        public Guid RecordId { get; init; }
        public Guid FieldId { get; init; }
        public Guid CatalogId { get; init; }
        public Guid ItemId { get; init; }
        public string Label { get; init; } = "";
    }
}
