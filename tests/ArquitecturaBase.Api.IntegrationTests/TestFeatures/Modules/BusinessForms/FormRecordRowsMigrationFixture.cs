using System.Text.Json;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

// A published version with a block of rows: the group question (from 0 to 3 items) and its two questions, an option of the
// catalog and a quantity. Its response, when there is one, chooses the same option in every item.
internal sealed record FormRecordRowsMigrationFixture(Guid DefinitionId, Guid VersionId, Guid? RecordId, Guid? SourceReceiptId,
    Guid GroupId, Guid ItemFieldId, Guid QuantityFieldId, string NameJson, string SchemaJson)
{
    public const string Before = "20261107004318_AddRecordAndLoginAuditIndexes";
    public const string WithRows = "20261107193701_AddFormRecordRows";

    public static async Task<FormRecordRowsMigrationFixture> AddBlockAsync(ApplicationDbContext db, FormCatalogValueMigrationFixture catalog,
        DateTime nowUtc, int? items, CancellationToken ct)
    {
        var definitionId = Guid.CreateVersion7(); var versionId = Guid.CreateVersion7();
        var groupId = Guid.CreateVersion7(); var itemFieldId = Guid.CreateVersion7(); var quantityFieldId = Guid.CreateVersion7();
        var group = new { fieldId = groupId, key = Key(groupId), type = "group", label = new { es = "Artículos" }, help = (object?)null,
            required = false, maxLength = (int?)null, options = (object?)null, rows = new { minRows = 0, maxRows = 3 } };
        var item = new { fieldId = itemFieldId, key = Key(itemFieldId), type = "choice", label = new { es = "Artículo" },
            help = (object?)null, required = true, maxLength = (int?)null, options = (object?)null,
            source = new { catalog = catalog.CatalogId }, repeated = true };
        var quantity = new { fieldId = quantityFieldId, key = Key(quantityFieldId), type = "decimal", label = new { es = "Cantidad" },
            help = (object?)null, required = true, maxLength = (int?)null, options = (object?)null, repeated = true };
        var questions = new object[] { group, item, quantity };
        var draft = JsonSerializer.Serialize(new { format = 1, fields = questions }, JsonSerializerOptions.Web);
        var schema = JsonSerializer.Serialize(new { format = 1, questions }, JsonSerializerOptions.Web);
        const string names = """{"es":"Recepción por ítems"}""";
        const string submit = """{"es":"Enviar"}""";
        var fingerprint = new string('c', 64);
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO "FormDefinitions" ("Id", "Name", "NameJson", "Prefix", "Culture", "SubmitLabelJson", "SchemaJson",
                "Revision", "QuestionCount", "PrefixLocked", "LastRecordNumber", "CreatedAtUtc", "OfferWhen")
            VALUES ({definitionId}, 'Recepción por ítems', CAST({names} AS jsonb), 'ROWS', 'es', CAST({submit} AS jsonb),
                CAST({draft} AS jsonb), 1, 2, TRUE, {(items is null ? 0 : 1)}, {nowUtc}, '');
            INSERT INTO "BusinessFormVersions" ("Id", "DefinitionId", "Number", "ContentHash", "SchemaFormatVersion", "SchemaJson",
                "NameJson", "SubmitLabelJson", "Culture", "QuestionCount", "CreatedAtUtc", "PublishedCultures")
            VALUES ({versionId}, {definitionId}, 1, {fingerprint}, 1, CAST({schema} AS jsonb), CAST({names} AS jsonb),
                CAST({submit} AS jsonb), 'es', 2, {nowUtc}, ARRAY['es']);
            UPDATE "FormDefinitions" SET "ActiveVersionId" = {versionId} WHERE "Id" = {definitionId};
            INSERT INTO "BusinessFormFields" ("Id", "DefinitionId", "StableKey", "Type")
            VALUES ({groupId}, {definitionId}, {group.key}, 'group'), ({itemFieldId}, {definitionId}, {item.key}, 'choice'),
                ({quantityFieldId}, {definitionId}, {quantity.key}, 'decimal');
            """, ct);
        if (items is not { } count) return new(definitionId, versionId, null, null, groupId, itemFieldId, quantityFieldId, names, schema);

        var recordId = Guid.CreateVersion7(); var receiptId = Guid.CreateVersion7(); var sourceReceiptId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var rows = Enumerable.Range(0, count).Select(row => new object[]
        {
            new { fieldId = itemFieldId, value = catalog.ItemId }, new { fieldId = quantityFieldId, value = 100m * (row + 1) },
        }).ToArray();
        var values = JsonSerializer.Serialize(new[] { new { fieldId = groupId, value = rows } }, JsonSerializerOptions.Web);
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO "BusinessFormRecords" ("Id", "DefinitionId", "VersionId", "Number", "ActorUserId", "SourceReceiptId",
                "SubmittedAtUtc", "ValuesJson", "CreatedAtUtc", "State")
            VALUES ({recordId}, {definitionId}, {versionId}, 1, {actorId}, {sourceReceiptId}, {nowUtc}, CAST({values} AS jsonb), {nowUtc}, 'Received');
            INSERT INTO "BusinessFormCaptureReceipts" ("Id", "SourceReceiptId", "DefinitionId", "VersionId", "Fingerprint",
                "RecordId", "Number", "State", "CreatedAtUtc")
            VALUES ({receiptId}, {sourceReceiptId}, {definitionId}, {versionId}, {fingerprint}, {recordId}, 1, 'Completed', {nowUtc});
            """, ct);
        for (var row = 0; row < count; row++)
        {
            var quantityValue = 100m * (row + 1);
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO "BusinessFormRecordValues" ("Id", "DefinitionId", "RecordId", "FieldId", "Type", "ChoiceId", "Row")
                VALUES ({Guid.CreateVersion7()}, {definitionId}, {recordId}, {itemFieldId}, 'choice', {catalog.ItemId}, {row});
                INSERT INTO "BusinessFormRecordValues" ("Id", "DefinitionId", "RecordId", "FieldId", "Type", "DecimalValue", "Row")
                VALUES ({Guid.CreateVersion7()}, {definitionId}, {recordId}, {quantityFieldId}, 'decimal', {quantityValue}, {row});
                INSERT INTO "BusinessFormRecordReferences" ("Id", "RecordId", "DefinitionId", "FieldId", "CatalogId", "CatalogRevision",
                    "ItemId", "Text", "CatalogName", "Row")
                VALUES ({Guid.CreateVersion7()}, {recordId}, {definitionId}, {itemFieldId}, {catalog.CatalogId}, 1, {catalog.ItemId}, 'Norte', 'Sucursales', {row});
                """, ct);
        }
        return new(definitionId, versionId, recordId, sourceReceiptId, groupId, itemFieldId, quantityFieldId, names, schema);
    }

    // A rejection as the previous schema stored it: the rejected question, without an item.
    public static async Task<Guid> AddRejectionAsync(ApplicationDbContext db, DateTime nowUtc, CancellationToken ct)
    {
        var receiptId = Guid.CreateVersion7();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO "BusinessFormCaptureReceipts" ("Id", "SourceReceiptId", "DefinitionId", "VersionId", "Fingerprint",
                "State", "ErrorCode", "RejectedFieldId", "CreatedAtUtc")
            VALUES ({receiptId}, {Guid.CreateVersion7()}, {Guid.CreateVersion7()}, {Guid.CreateVersion7()}, {new string('e', 64)},
                'Rejected', 'Forms.Record.CatalogItemInactive', {Guid.CreateVersion7()}, {nowUtc});
            """, ct);
        return receiptId;
    }

    // The captured facts without the columns AddFormRecordRows adds, so the same projection reads both schemas.
    public static Task<string[]> HistoryAsync(ApplicationDbContext db, CancellationToken ct) => db.Database.SqlQueryRaw<string>("""
        SELECT jsonb_build_object('table', 'definition', 'data', to_jsonb(r))::text AS "Value" FROM "FormDefinitions" r
        UNION ALL SELECT jsonb_build_object('table', 'version', 'data', to_jsonb(r))::text FROM "BusinessFormVersions" r
        UNION ALL SELECT jsonb_build_object('table', 'field', 'data', to_jsonb(r))::text FROM "BusinessFormFields" r
        UNION ALL SELECT jsonb_build_object('table', 'record', 'data', to_jsonb(r))::text FROM "BusinessFormRecords" r
        UNION ALL SELECT jsonb_build_object('table', 'value', 'data', to_jsonb(r) - 'Row')::text FROM "BusinessFormRecordValues" r
        UNION ALL SELECT jsonb_build_object('table', 'reference', 'data', to_jsonb(r) - 'Row')::text FROM "BusinessFormRecordReferences" r
        UNION ALL SELECT jsonb_build_object('table', 'receipt', 'data', to_jsonb(r) - 'RejectedRow')::text FROM "BusinessFormCaptureReceipts" r
        UNION ALL SELECT jsonb_build_object('table', 'catalog', 'data', to_jsonb(r))::text FROM "BusinessFormCatalogs" r
        UNION ALL SELECT jsonb_build_object('table', 'item', 'data', to_jsonb(r))::text FROM "BusinessFormCatalogItems" r
        ORDER BY "Value"
        """).ToArrayAsync(ct);

    public static Task<string[]> AllFactsAsync(ApplicationDbContext db, CancellationToken ct) => db.Database.SqlQueryRaw<string>("""
        SELECT jsonb_build_object('table', 'definition', 'data', to_jsonb(r))::text AS "Value" FROM "FormDefinitions" r
        UNION ALL SELECT jsonb_build_object('table', 'version', 'data', to_jsonb(r))::text FROM "BusinessFormVersions" r
        UNION ALL SELECT jsonb_build_object('table', 'field', 'data', to_jsonb(r))::text FROM "BusinessFormFields" r
        UNION ALL SELECT jsonb_build_object('table', 'record', 'data', to_jsonb(r))::text FROM "BusinessFormRecords" r
        UNION ALL SELECT jsonb_build_object('table', 'value', 'data', to_jsonb(r))::text FROM "BusinessFormRecordValues" r
        UNION ALL SELECT jsonb_build_object('table', 'reference', 'data', to_jsonb(r))::text FROM "BusinessFormRecordReferences" r
        UNION ALL SELECT jsonb_build_object('table', 'receipt', 'data', to_jsonb(r))::text FROM "BusinessFormCaptureReceipts" r
        ORDER BY "Value"
        """).ToArrayAsync(ct);

    private static string Key(Guid fieldId) => "field_" + fieldId.ToString("N");
}
