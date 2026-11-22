using System.Linq.Expressions;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;
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
        var records = dbContext.Set<BusinessFormRecord>();
        // The channel «documents» of a response passed from the inbox (that of its first version), which the sheet shows as «—».
        var passed = dbContext.Set<FormReceivedDocument>().Where(document => document.ReviewedByUserId != null && document.ReviewedAtUtc != null);
        var unresolved = FormResponseQuery.Unresolved(dbContext);
        // OT1-C: every column with one datum, by what its cell shows; the empty values go last (ApplySort).
        var sortable = new Dictionary<string, Expression<Func<BusinessFormRecord, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["number"] = record => record.Number,
            ["corrected"] = record => record.ReplacesRecordId != null,
            ["submittedAtUtc"] = record => record.SubmittedAtUtc,
            ["actor"] = record => dbContext.Users.Where(user => user.Id == record.ActorUserId).Select(user => user.DisplayName).FirstOrDefault(),
            // «App», then a channel's («WhatsApp»); a response passed from a document shows «—».
            ["channel"] = record => record.EnteredInApp ? 0 : passed.Any(document => records.Any(first => first.Id == document.RecordId
                && first.SourceReceiptId == (record.OriginalReceiptId ?? record.SourceReceiptId))) ? null : (int?)1,
            ["state"] = record => record.State,
            ["rules"] = record => unresolved.Any(run => run.RecordId == record.Id),
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
            if (request.SortCatalogFieldId is { } catalogFieldId)
                sortable["catalogField"] = CatalogFieldSort(definition, fieldId, catalogFieldId, request.DisplayCulture);
        }
        if (request.SortResultId is { } resultId) sortable["result"] = ResultSort(definition, resultId);
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
    // A field of the catalog of a question of one option, by the value its response sealed (FieldValuesJson): an option by its text in
    // the version's language, then in the other one; a decimal as a number; anything else as its JSON value (number, yes or no, text).
    // As jsonb, each kind compares as its own type.
    private Expression<Func<BusinessFormRecord, object?>> CatalogFieldSort(FormResponseDefinitionRow definition, Guid fieldId, Guid catalogFieldId, string culture)
    {
        var values = dbContext.Database.SqlQueryRaw<CatalogFieldValueRow>("""
            SELECT reference."RecordId", CASE field.value->>'type'
                WHEN 'catalog' THEN to_jsonb(COALESCE(field.value->'text'->>(presentation.value->>'culture'),
                    field.value->'text'->>(CASE presentation.value->>'culture' WHEN 'en' THEN 'es' ELSE 'en' END)))
                WHEN 'decimal' THEN to_jsonb((field.value->>'value')::numeric)
                ELSE field.value->'value' END AS "Value"
            FROM "BusinessFormRecordReferences" reference
            JOIN "BusinessFormRecords" record ON record."Id" = reference."RecordId"
            JOIN jsonb_array_elements({2}::jsonb) presentation(value) ON (presentation.value->>'versionId')::uuid = record."VersionId"
            CROSS JOIN LATERAL jsonb_array_elements(reference."FieldValuesJson") field(value)
            WHERE reference."FieldId" = {0} AND field.value->>'id' = {1}
            """.ReplaceLineEndings("\n"), fieldId, catalogFieldId.ToString("D"), FormResponseQuery.Presentations(definition, culture));
        return record => values.Where(value => value.RecordId == record.Id).Select(value => value.Value).FirstOrDefault();
    }
    // RE1-A: a result column by the latest value of its datum in the response, as the sheet shows it; a number or an amount as a number.
    private Expression<Func<BusinessFormRecord, object?>> ResultSort(FormResponseDefinitionRow definition, string columnId)
    {
        var dot = columnId.LastIndexOf('.');
        var values = dbContext.Database.SqlQueryRaw<ResultValueRow>("""
            SELECT kept."RecordId", kept."RecordedAtUtc", kept."Id",
                CASE WHEN item.value->>'kind' IN ('number', 'amount') THEN to_jsonb((item.value->>'value')::numeric) ELSE to_jsonb(item.value->>'value') END AS "Value"
            FROM "BusinessFormRecordResults" kept
            CROSS JOIN LATERAL jsonb_array_elements(kept."ValuesJson") item(value)
            WHERE kept."DefinitionId" = {0} AND kept."Source" = {1} AND item.value->>'key' = {2}
            """.ReplaceLineEndings("\n"), definition.Id, columnId[..dot], columnId[(dot + 1)..]);
        return record => values.Where(value => value.RecordId == record.Id).OrderByDescending(value => value.RecordedAtUtc)
            .ThenByDescending(value => value.Id).Select(value => value.Value).FirstOrDefault();
    }
    // Value is jsonb in SQL: the sort compares it there and never reads it.
    private sealed class CatalogFieldValueRow
    {
        public Guid RecordId { get; init; }
        public string? Value { get; init; }
    }
    private sealed class ResultValueRow
    {
        public Guid RecordId { get; init; }
        public DateTime RecordedAtUtc { get; init; }
        public Guid Id { get; init; }
        public string? Value { get; init; }
    }
    private sealed class ChoiceLabelRow
    {
        public Guid VersionId { get; init; }
        public Guid ChoiceId { get; init; }
        public string Label { get; init; } = "";
    }
}
