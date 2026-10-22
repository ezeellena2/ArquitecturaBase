using System.Text.Json;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

internal sealed record FormMultipleChoiceMigrationFixture(Guid RecordId, Guid FieldId, Guid? ValueId, Guid? ReferenceId, Guid? ItemId)
{
    public const string Before = "20261022114007_AddCatalogImportMarker";

    public static async Task<FormMultipleChoiceMigrationFixture> AddCapturedAsync(ApplicationDbContext db,
        FormCatalogValueMigrationFixture catalog, DateTime nowUtc, int count, CancellationToken ct)
    {
        var definitionId = Guid.CreateVersion7(); var versionId = Guid.CreateVersion7(); var fieldId = Guid.CreateVersion7();
        var recordId = Guid.CreateVersion7(); var receiptId = Guid.CreateVersion7(); var sourceReceiptId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7(); var firstValue = Guid.CreateVersion7(); var firstReference = Guid.CreateVersion7();
        var choices = Enumerable.Range(0, count).Select(index => index == 0 ? catalog.ItemId : Guid.CreateVersion7()).ToArray();
        var field = new { fieldId, key = "field_" + fieldId.ToString("N"), type = "multiChoice",
            label = new { es = "Sucursales" }, required = count > 0, source = new { catalog = catalog.CatalogId },
            selection = new { minSelected = count > 0 ? 1 : 0, maxSelected = 3 } };
        var draft = JsonSerializer.Serialize(new { format = 1, fields = new[] { field } }, JsonSerializerOptions.Web);
        var schema = JsonSerializer.Serialize(new { format = 1, questions = new[] { field } }, JsonSerializerOptions.Web);
        var values = JsonSerializer.Serialize(new[] { new { fieldId, value = choices } }, JsonSerializerOptions.Web);
        const string names = """{"es":"Visita múltiple"}""";
        const string submit = """{"es":"Enviar"}""";
        var fingerprint = new string('b', 64);
        // New captured facts are born with their own schema; no previous version or receipt is rewritten.
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO "FormDefinitions" ("Id", "Name", "NameJson", "Prefix", "Culture", "SubmitLabelJson", "SchemaJson",
                "Revision", "QuestionCount", "PrefixLocked", "LastRecordNumber", "CreatedAtUtc", "OfferWhen")
            VALUES ({definitionId}, 'Visita múltiple', CAST({names} AS jsonb), 'MULTICAPTURE', 'es', CAST({submit} AS jsonb),
                CAST({draft} AS jsonb), 1, 1, TRUE, 1, {nowUtc}, 'Cuando registra una visita');
            INSERT INTO "BusinessFormVersions" ("Id", "DefinitionId", "Number", "ContentHash", "SchemaFormatVersion", "SchemaJson",
                "NameJson", "SubmitLabelJson", "Culture", "QuestionCount", "CreatedAtUtc", "PublishedCultures")
            VALUES ({versionId}, {definitionId}, 1, {fingerprint}, 1, CAST({schema} AS jsonb), CAST({names} AS jsonb),
                CAST({submit} AS jsonb), 'es', 1, {nowUtc}, ARRAY['es']);
            UPDATE "FormDefinitions" SET "ActiveVersionId" = {versionId} WHERE "Id" = {definitionId};
            INSERT INTO "BusinessFormFields" ("Id", "DefinitionId", "StableKey", "Type")
            VALUES ({fieldId}, {definitionId}, {field.key}, 'multiChoice');
            INSERT INTO "BusinessFormRecords" ("Id", "DefinitionId", "VersionId", "Number", "ActorUserId", "SourceReceiptId",
                "SubmittedAtUtc", "ValuesJson", "CreatedAtUtc", "State")
            VALUES ({recordId}, {definitionId}, {versionId}, 1, {actorId}, {sourceReceiptId}, {nowUtc}, CAST({values} AS jsonb), {nowUtc}, 'Received');
            INSERT INTO "BusinessFormCaptureReceipts" ("Id", "SourceReceiptId", "DefinitionId", "VersionId", "Fingerprint",
                "RecordId", "Number", "State", "CreatedAtUtc")
            VALUES ({receiptId}, {sourceReceiptId}, {definitionId}, {versionId}, {fingerprint}, {recordId}, 1, 'Completed', {nowUtc});
            """, ct);
        for (var position = 0; position < count; position++)
        {
            var itemId = choices[position]; var valueId = position == 0 ? firstValue : Guid.CreateVersion7();
            var referenceId = position == 0 ? firstReference : Guid.CreateVersion7();
            if (position > 0)
            {
                var code = "MULTI-" + position;
                await db.Database.ExecuteSqlAsync($"""
                    INSERT INTO "BusinessFormCatalogItems" ("Id", "CatalogId", "Code", "TextEs", "Position", "IsActive", "CreatedAtUtc")
                    VALUES ({itemId}, {catalog.CatalogId}, {code}, 'Sur', {position + 1}, TRUE, {nowUtc});
                    """, ct);
            }
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO "BusinessFormRecordValues" ("Id", "DefinitionId", "RecordId", "FieldId", "Type", "ChoiceId", "Position")
                VALUES ({valueId}, {definitionId}, {recordId}, {fieldId}, 'multiChoice', {itemId}, {position});
                INSERT INTO "BusinessFormRecordReferences" ("Id", "RecordId", "DefinitionId", "FieldId", "CatalogId", "CatalogRevision",
                    "ItemId", "Text", "CatalogName")
                VALUES ({referenceId}, {recordId}, {definitionId}, {fieldId}, {catalog.CatalogId}, 1, {itemId}, 'Sucursal', 'Sucursales');
                """, ct);
        }
        return new(recordId, fieldId, count > 0 ? firstValue : null, count > 0 ? firstReference : null, count > 0 ? choices[0] : null);
    }

    public static Task<string[]> AllCapturedRowsAsync(ApplicationDbContext db, CancellationToken ct) => db.Database.SqlQueryRaw<string>("""
        SELECT jsonb_build_object('table', 'definition', 'data', to_jsonb(r))::text AS "Value" FROM "FormDefinitions" r
        UNION ALL SELECT jsonb_build_object('table', 'version', 'data', to_jsonb(r))::text FROM "BusinessFormVersions" r
        UNION ALL SELECT jsonb_build_object('table', 'field', 'data', to_jsonb(r))::text FROM "BusinessFormFields" r
        UNION ALL SELECT jsonb_build_object('table', 'record', 'data', to_jsonb(r))::text FROM "BusinessFormRecords" r
        UNION ALL SELECT jsonb_build_object('table', 'value', 'data', to_jsonb(r))::text FROM "BusinessFormRecordValues" r
        UNION ALL SELECT jsonb_build_object('table', 'reference', 'data', to_jsonb(r))::text FROM "BusinessFormRecordReferences" r
        UNION ALL SELECT jsonb_build_object('table', 'receipt', 'data', to_jsonb(r))::text FROM "BusinessFormCaptureReceipts" r
        ORDER BY "Value"
        """).ToArrayAsync(ct);
}
