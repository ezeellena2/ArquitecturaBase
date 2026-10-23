using System.Text.Json;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

internal sealed record FormMultipleChoiceMigrationFixture(Guid RecordId, Guid FieldId, Guid? ValueId, Guid? ReferenceId, Guid? ItemId)
{
    public const string Before = "20261022114007_AddCatalogImportMarker";
    public const string WithMultipleChoices = "20261022125008_AddFormMultipleChoiceValues";

    public static async Task<Guid> AddDraftAsync(ApplicationDbContext db, FormCatalogValueMigrationFixture catalog,
        DateTime nowUtc, bool publishedScalar, CancellationToken ct)
    {
        var definitionId = Guid.CreateVersion7(); var draftFieldId = Guid.CreateVersion7();
        var field = new { fieldId = draftFieldId, key = "field_" + draftFieldId.ToString("N"), type = "multiChoice",
            label = new { es = "Sucursales" }, required = false, source = new { catalog = catalog.CatalogId },
            selection = new { minSelected = 0, maxSelected = 3 } };
        var draft = JsonSerializer.Serialize(new { format = 1, fields = new[] { field } }, JsonSerializerOptions.Web);
        const string names = """{"es":"Borrador múltiple"}""";
        const string submit = """{"es":"Enviar"}""";
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO "FormDefinitions" ("Id", "Name", "NameJson", "Prefix", "Culture", "SubmitLabelJson", "SchemaJson",
                "Revision", "QuestionCount", "PrefixLocked", "LastRecordNumber", "CreatedAtUtc", "OfferWhen")
            VALUES ({definitionId}, 'Borrador múltiple', CAST({names} AS jsonb), 'MULTIDRAFT', 'es', CAST({submit} AS jsonb),
                CAST({draft} AS jsonb), 1, 1, {publishedScalar}, 0, {nowUtc}, '');
            """, ct);
        if (publishedScalar)
        {
            var versionId = Guid.CreateVersion7(); var scalarFieldId = Guid.CreateVersion7();
            var scalar = new { fieldId = scalarFieldId, key = "field_" + scalarFieldId.ToString("N"), type = "shortText",
                label = new { es = "Lugar" }, required = false, maxLength = 80 };
            var schema = JsonSerializer.Serialize(new { format = 1, questions = new[] { scalar } }, JsonSerializerOptions.Web);
            var fingerprint = new string('d', 64);
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO "BusinessFormVersions" ("Id", "DefinitionId", "Number", "ContentHash", "SchemaFormatVersion", "SchemaJson",
                    "NameJson", "SubmitLabelJson", "Culture", "QuestionCount", "CreatedAtUtc", "PublishedCultures")
                VALUES ({versionId}, {definitionId}, 1, {fingerprint}, 1, CAST({schema} AS jsonb), CAST({names} AS jsonb),
                    CAST({submit} AS jsonb), 'es', 1, {nowUtc}, ARRAY['es']);
                UPDATE "FormDefinitions" SET "ActiveVersionId" = {versionId} WHERE "Id" = {definitionId};
                INSERT INTO "BusinessFormFields" ("Id", "DefinitionId", "StableKey", "Type")
                VALUES ({scalarFieldId}, {definitionId}, {scalar.key}, 'shortText');
                """, ct);
        }
        return definitionId;
    }

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

    // Only columns present before AddFormMultipleChoiceValues participate in the Up/Down comparison.
    public static Task<string[]> LegacyRowsAsync(ApplicationDbContext db, CancellationToken ct) => db.Database.SqlQueryRaw<string>("""
        SELECT jsonb_build_object('table', 'definition', 'data', to_jsonb(r))::text AS "Value" FROM "FormDefinitions" r
        UNION ALL SELECT jsonb_build_object('table', 'version', 'data', to_jsonb(r))::text FROM "BusinessFormVersions" r
        UNION ALL SELECT jsonb_build_object('table', 'operation', 'data', to_jsonb(r))::text FROM "BusinessFormPublicationOperations" r
        UNION ALL SELECT jsonb_build_object('table', 'field', 'data', to_jsonb(r))::text FROM "BusinessFormFields" r
        UNION ALL SELECT jsonb_build_object('table', 'record', 'data', to_jsonb(r))::text FROM "BusinessFormRecords" r
        UNION ALL SELECT jsonb_build_object('table', 'recordValue', 'data', jsonb_build_object(
            'Id', r."Id", 'DefinitionId', r."DefinitionId", 'RecordId', r."RecordId", 'FieldId', r."FieldId", 'Type', r."Type",
            'TextValue', r."TextValue", 'DecimalValue', r."DecimalValue", 'DateValue', r."DateValue", 'BooleanValue', r."BooleanValue", 'ChoiceId', r."ChoiceId"))::text
            FROM "BusinessFormRecordValues" r
        UNION ALL SELECT jsonb_build_object('table', 'receipt', 'data', to_jsonb(r))::text FROM "BusinessFormCaptureReceipts" r
        UNION ALL SELECT jsonb_build_object('table', 'catalog', 'data', jsonb_build_object(
            'Id', r."Id", 'Key', r."Key", 'Kind', r."Kind", 'NameJson', r."NameJson", 'DescriptionJson', r."DescriptionJson",
            'FiltersJson', r."FiltersJson", 'IsActive', r."IsActive", 'Revision', r."Revision", 'CreatedAtUtc', r."CreatedAtUtc",
            'CreatedBy', r."CreatedBy", 'ModifiedAtUtc', r."ModifiedAtUtc", 'ModifiedBy', r."ModifiedBy", 'ProposalId', r."ProposalId",
            'ProposalEvidenceJson', r."ProposalEvidenceJson", 'ProposalState', r."ProposalState", 'ProposalDecidedByUserId', r."ProposalDecidedByUserId",
            'ProposalDecidedAtUtc', r."ProposalDecidedAtUtc", 'DisplayNameEs', r."DisplayNameEs", 'DisplayNameEn', r."DisplayNameEn",
            'DisplayDescriptionEs', r."DisplayDescriptionEs", 'DisplayDescriptionEn', r."DisplayDescriptionEn", 'ValueType', r."ValueType",
            'LastImportSha256', r."LastImportSha256", 'LastImportAtUtc', r."LastImportAtUtc"))::text FROM "BusinessFormCatalogs" r
        UNION ALL SELECT jsonb_build_object('table', 'item', 'data', jsonb_build_object(
            'Id', r."Id", 'CatalogId', r."CatalogId", 'Code', r."Code", 'Value', r."Value", 'TextEs', r."TextEs", 'TextEn', r."TextEn",
            'DetailEs', r."DetailEs", 'DetailEn', r."DetailEn", 'Position', r."Position", 'IsActive', r."IsActive",
            'CreatedAtUtc', r."CreatedAtUtc", 'CreatedBy', r."CreatedBy", 'ModifiedAtUtc', r."ModifiedAtUtc", 'ModifiedBy', r."ModifiedBy"))::text
            FROM "BusinessFormCatalogItems" r
        UNION ALL SELECT jsonb_build_object('table', 'reference', 'data', jsonb_build_object(
            'Id', r."Id", 'RecordId', r."RecordId", 'DefinitionId', r."DefinitionId", 'FieldId', r."FieldId", 'CatalogId', r."CatalogId",
            'CatalogRevision', r."CatalogRevision", 'ItemId', r."ItemId", 'Text', r."Text", 'Detail', r."Detail",
            'CatalogName', r."CatalogName", 'TranslationsJson', r."TranslationsJson", 'Value', r."Value", 'ValueType', r."ValueType"))::text
            FROM "BusinessFormRecordReferences" r
        ORDER BY "Value"
        """).ToArrayAsync(ct);

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
