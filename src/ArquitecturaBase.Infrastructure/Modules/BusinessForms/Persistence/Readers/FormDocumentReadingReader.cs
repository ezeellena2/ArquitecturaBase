using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

/// <summary>
/// «Cómo viene leyendo» (DOC1) in fixed SQL, bounded by the form and the window: the responses whose capture origin carries the
/// document they came from (<c>origin.document</c>, which the capture of a document sets), each value the reading prefilled (proposed
/// and included, <c>origin.includedFieldIds</c>: one the person left out was neither read into the form nor kept) against the
/// questions changed when confirming (<c>changedFieldIds</c>). One statement per family, without a query per response.
/// </summary>
internal sealed class FormDocumentReadingReader(ApplicationDbContext dbContext) : IFormDocumentReadingReader
{
    private const string FromDocuments = "\nFROM \"BusinessFormRecords\" AS record";

    private const string ProposedValues = """

        CROSS JOIN LATERAL jsonb_array_elements(CASE WHEN jsonb_typeof(record."CaptureOriginJson" -> 'origin' -> 'proposedValues') = 'array'
            THEN record."CaptureOriginJson" -> 'origin' -> 'proposedValues' ELSE '[]'::jsonb END) AS proposed(value)
        """;

    private const string WhereDocuments = """

        WHERE record."DefinitionId" = {0} AND record."SubmittedAtUtc" >= {1}
          AND COALESCE(jsonb_typeof(record."CaptureOriginJson" -> 'origin' -> 'document'), 'null') <> 'null'
        """;

    private const string Included = """

          AND COALESCE(record."CaptureOriginJson" -> 'origin' -> 'includedFieldIds', '[]'::jsonb) @> jsonb_build_array(proposed.value ->> 'fieldId')
        """;

    private const string Changed = """COALESCE(record."CaptureOriginJson" -> 'changedFieldIds', '[]'::jsonb) @> jsonb_build_array(proposed.value ->> 'fieldId')""";

    private const string CountSql = "SELECT COUNT(*)::integer AS \"Value\"" + FromDocuments + WhereDocuments;

    private const string QuestionsSql = """
        SELECT (proposed.value ->> 'fieldId')::uuid AS "FieldId", COUNT(*)::integer AS "Read",
            (COUNT(*) FILTER (WHERE NOT (
        """ + Changed + """)))::integer AS "Unchanged" """ + FromDocuments + ProposedValues + WhereDocuments + Included + """

        GROUP BY proposed.value ->> 'fieldId'
        ORDER BY 1
        """;

    // The texts of a catalog option: the one proposed, as the capture kept it in its origin, and the one confirmed, from its reference.
    private const string CorrectionsSql = """
        SELECT record."Id" AS "RecordId", record."Number" AS "Number", record."VersionId" AS "VersionId",
            record."SubmittedAtUtc" AS "SubmittedAtUtc", (proposed.value ->> 'fieldId')::uuid AS "FieldId", proposed.value ->> 'value' AS "Read",
            (SELECT confirmed.value ->> 'value'
             FROM jsonb_array_elements(CASE WHEN jsonb_typeof(record."ValuesJson") = 'array' THEN record."ValuesJson" ELSE '[]'::jsonb END) AS confirmed(value)
             WHERE confirmed.value ->> 'fieldId' = proposed.value ->> 'fieldId'
             LIMIT 1) AS "Confirmed",
            (SELECT reference.value ->> 'text'
             FROM jsonb_array_elements(CASE WHEN jsonb_typeof(record."CaptureOriginJson" -> 'proposedReferences') = 'array'
                 THEN record."CaptureOriginJson" -> 'proposedReferences' ELSE '[]'::jsonb END) AS reference(value)
             WHERE reference.value ->> 'fieldId' = proposed.value ->> 'fieldId' AND reference.value ->> 'itemId' = proposed.value ->> 'value'
             LIMIT 1) AS "ReadText",
            (SELECT reference."Text" FROM "BusinessFormRecordReferences" AS reference
             WHERE reference."RecordId" = record."Id" AND reference."FieldId" = (proposed.value ->> 'fieldId')::uuid
             ORDER BY reference."Row" LIMIT 1) AS "ConfirmedText"
        """ + FromDocuments + ProposedValues + WhereDocuments + Included + "\n  AND " + Changed + """

        ORDER BY record."SubmittedAtUtc" DESC, record."Id" DESC, "FieldId"
        LIMIT {2}
        """;

    public Task<FormDocumentReadingDefinitionRow?> FindDefinitionAsync(Guid definitionId, CancellationToken cancellationToken) =>
        dbContext.Set<BusinessFormDefinition>().AsNoTracking().Where(definition => definition.Id == definitionId)
            .Select(definition => new FormDocumentReadingDefinitionRow(definition.Prefix, definition.StandardKey, definition.StandardVersion,
                definition.StandardBindingsJson)).SingleOrDefaultAsync(cancellationToken);

    public async Task<FormDocumentReadingStatsRow> FindStatsAsync(Guid definitionId, DateTime sinceUtc, CancellationToken cancellationToken)
    {
        var documents = await dbContext.Database.SqlQueryRaw<int>(CountSql, definitionId, sinceUtc).ToListAsync(cancellationToken);
        var questions = await dbContext.Database.SqlQueryRaw<QuestionSqlRow>(QuestionsSql, definitionId, sinceUtc).ToListAsync(cancellationToken);
        return new(documents.Single(), questions.Select(row => new FormDocumentReadingQuestionResponse(row.FieldId, row.Read, row.Unchanged)).ToArray());
    }

    public async Task<IReadOnlyList<FormDocumentReadingCorrectionRow>> ListCorrectionsAsync(Guid definitionId, DateTime sinceUtc, int limit,
        CancellationToken cancellationToken) =>
        (await dbContext.Database.SqlQueryRaw<CorrectionSqlRow>(CorrectionsSql, definitionId, sinceUtc, limit).ToListAsync(cancellationToken))
        .Select(row => new FormDocumentReadingCorrectionRow(row.RecordId, row.Number, row.VersionId, row.SubmittedAtUtc, row.FieldId, row.Read,
            row.Confirmed, row.ReadText, row.ConfirmedText)).ToArray();

    public async Task<IReadOnlyDictionary<Guid, string>> ListVersionSchemasAsync(IReadOnlyCollection<Guid> versionIds, CancellationToken cancellationToken) =>
        await dbContext.Set<BusinessFormVersion>().AsNoTracking().Where(version => versionIds.Contains(version.Id))
            .ToDictionaryAsync(version => version.Id, version => version.SchemaJson, cancellationToken);

    private sealed class QuestionSqlRow
    {
        public Guid FieldId { get; init; }
        public int Read { get; init; }
        public int Unchanged { get; init; }
    }

    private sealed class CorrectionSqlRow
    {
        public Guid RecordId { get; init; }
        public long Number { get; init; }
        public Guid VersionId { get; init; }
        public DateTime SubmittedAtUtc { get; init; }
        public Guid FieldId { get; init; }
        public string? Read { get; init; }
        public string? Confirmed { get; init; }
        public string? ReadText { get; init; }
        public string? ConfirmedText { get; init; }
    }
}
