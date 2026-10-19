using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal sealed partial class FormResponseReader
{
    public async Task<IReadOnlyList<Guid>> ListAncestorDefinitionIdsAsync(Guid recordId, CancellationToken ct)
    {
        var rows = await dbContext.Database.SqlQueryRaw<AncestorRow>("""
            WITH RECURSIVE ancestors AS (
                SELECT "Id", "DefinitionId", "OriginRecordId" FROM "BusinessFormRecords" WHERE "Id" = @recordId
                UNION ALL
                SELECT parent."Id", parent."DefinitionId", parent."OriginRecordId"
                FROM "BusinessFormRecords" parent JOIN ancestors child ON parent."Id" = child."OriginRecordId"
            )
            SELECT DISTINCT "DefinitionId" FROM ancestors
            """, new NpgsqlParameter("recordId", recordId)).ToArrayAsync(ct);
        return rows.Select(row => row.DefinitionId).ToArray();
    }

    public async Task<IReadOnlyList<FormRecordChainStoredRow>> ListChainAsync(Guid recordId, CancellationToken ct)
    {
        var rows = await dbContext.Database.SqlQueryRaw<ChainRow>("""
            WITH RECURSIVE ancestors AS (
                SELECT "Id", "OriginRecordId" FROM "BusinessFormRecords" WHERE "Id" = @recordId
                UNION ALL
                SELECT parent."Id", parent."OriginRecordId"
                FROM "BusinessFormRecords" parent JOIN ancestors child ON parent."Id" = child."OriginRecordId"
            ), chain AS (
                SELECT record."Id", record."OriginRecordId" FROM "BusinessFormRecords" record
                JOIN ancestors root ON record."Id" = root."Id" WHERE root."OriginRecordId" IS NULL
                UNION ALL
                SELECT child."Id", child."OriginRecordId"
                FROM "BusinessFormRecords" child JOIN chain parent ON child."OriginRecordId" = parent."Id"
            )
            SELECT record."Id", record."DefinitionId", record."OriginRecordId", definition."Prefix", record."Number",
                   version."Id" AS "VersionId", version."NameJson", record."SubmittedAtUtc"
            FROM chain JOIN "BusinessFormRecords" record ON record."Id" = chain."Id"
            JOIN "FormDefinitions" definition ON definition."Id" = record."DefinitionId"
            JOIN "BusinessFormVersions" version ON version."Id" = record."VersionId"
            ORDER BY record."SubmittedAtUtc", record."Id"
            """, new NpgsqlParameter("recordId", recordId)).ToArrayAsync(ct);
        var versions = await records.ListVersionsAsync(rows.Select(row => row.VersionId).Distinct().ToArray(), ct);
        return rows.Select(row => new FormRecordChainStoredRow(row.Id, row.DefinitionId, row.OriginRecordId,
            row.Prefix, row.Number, row.NameJson, row.SubmittedAtUtc) { Version = versions[row.VersionId] }).ToArray();
    }

    private sealed class AncestorRow
    {
        public Guid DefinitionId { get; init; }
    }
    private sealed class ChainRow
    {
        public Guid Id { get; init; }
        public Guid DefinitionId { get; init; }
        public Guid? OriginRecordId { get; init; }
        public string Prefix { get; init; } = "";
        public long Number { get; init; }
        public Guid VersionId { get; init; }
        public string NameJson { get; init; } = "";
        public DateTime SubmittedAtUtc { get; init; }
    }
}
