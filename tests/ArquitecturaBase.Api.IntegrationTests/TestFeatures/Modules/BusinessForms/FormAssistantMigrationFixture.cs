using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

internal sealed record FormAssistantMigrationFixture(Guid DefinitionId, Guid VersionId, Guid RecordId, Guid FieldId,
    Guid ReceiptId, Guid CatalogId, Guid ActorId, string SchemaJson, string ValuesJson)
{
    public const string Before = "20261017192801_EnableWhatsAppAssistant";

    public static async Task<FormAssistantMigrationFixture> InsertAsync(ApplicationDbContext db, DateTime nowUtc, CancellationToken ct)
    {
        var definitionId = Guid.CreateVersion7(); var versionId = Guid.CreateVersion7(); var recordId = Guid.CreateVersion7();
        var fieldId = Guid.CreateVersion7(); var receiptId = Guid.CreateVersion7(); var catalogId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7(); var sourceReceiptId = Guid.CreateVersion7(); var valueId = Guid.CreateVersion7();
        var labels = new Dictionary<string, string> { ["es"] = "Confirmado", ["en"] = "Confirmed" };
        var field = new CanonicalFormField(fieldId, "field_" + fieldId.ToString("N"), "boolean", labels, null, true, null, null);
        var options = JsonSerializerOptions.Web;
        var draft = JsonSerializer.Serialize(new FormQuestionSchema(1, [field]), options);
        var schema = JsonSerializer.Serialize(new FormVersionSchema(1, [field]), options);
        var values = JsonSerializer.Serialize(new[] { new { FieldId = fieldId, Value = false } }, options);
        const string name = """{"es":"Visita histórica","en":"Historical visit"}""";
        const string submit = """{"es":"Enviar","en":"Submit"}""";
        const string catalogName = """{"es":"Sucursales","en":"Branches"}""";
        const string catalogDescription = """{"es":"Lista histórica","en":"Historical list"}""";
        var fingerprint = new string('a', 64);
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO "FormDefinitions" ("Id", "Name", "NameJson", "Prefix", "Culture", "SubmitLabelJson", "SchemaJson",
                "Revision", "QuestionCount", "PrefixLocked", "LastRecordNumber", "CreatedAtUtc", "OfferWhen")
            VALUES ({definitionId}, 'Visita histórica', CAST({name} AS jsonb), 'MIGVIS', 'es', CAST({submit} AS jsonb),
                CAST({draft} AS jsonb), 1, 1, TRUE, 1, {nowUtc}, 'Cuando informa una visita');
            INSERT INTO "BusinessFormVersions" ("Id", "DefinitionId", "Number", "ContentHash", "SchemaFormatVersion",
                "SchemaJson", "NameJson", "SubmitLabelJson", "Culture", "QuestionCount", "CreatedAtUtc")
            VALUES ({versionId}, {definitionId}, 1, {fingerprint}, 1, CAST({schema} AS jsonb), CAST({name} AS jsonb),
                CAST({submit} AS jsonb), 'es', 1, {nowUtc});
            UPDATE "FormDefinitions" SET "ActiveVersionId" = {versionId} WHERE "Id" = {definitionId};
            INSERT INTO "BusinessFormFields" ("Id", "DefinitionId", "StableKey", "Type")
            VALUES ({fieldId}, {definitionId}, {field.Key}, 'boolean');
            INSERT INTO "BusinessFormRecords" ("Id", "DefinitionId", "VersionId", "Number", "ActorUserId", "SourceReceiptId",
                "SubmittedAtUtc", "ValuesJson", "CreatedAtUtc", "State")
            VALUES ({recordId}, {definitionId}, {versionId}, 1, {actorId}, {sourceReceiptId}, {nowUtc}, CAST({values} AS jsonb), {nowUtc}, 'Received');
            INSERT INTO "BusinessFormRecordValues" ("Id", "DefinitionId", "RecordId", "FieldId", "Type", "BooleanValue")
            VALUES ({valueId}, {definitionId}, {recordId}, {fieldId}, 'boolean', FALSE);
            INSERT INTO "BusinessFormCaptureReceipts" ("Id", "SourceReceiptId", "DefinitionId", "VersionId", "Fingerprint",
                "RecordId", "Number", "State", "CreatedAtUtc")
            VALUES ({receiptId}, {sourceReceiptId}, {definitionId}, {versionId}, {fingerprint}, {recordId}, 1, 'Completed', {nowUtc});
            INSERT INTO "BusinessFormCatalogs" ("Id", "Key", "Kind", "NameJson", "DescriptionJson", "IsActive", "Revision", "CreatedAtUtc")
            VALUES ({catalogId}, 'MIGCAT', 'Manual', CAST({catalogName} AS jsonb), CAST({catalogDescription} AS jsonb), TRUE, 1, {nowUtc});
            """, ct);
        return new(definitionId, versionId, recordId, fieldId, receiptId, catalogId, actorId, schema, values);
    }

    public async Task AssertUnchangedAsync(ApplicationDbContext db, CancellationToken ct)
    {
        Assert.Equal(VersionId, await db.Database.SqlQuery<Guid>($"""SELECT "ActiveVersionId" AS "Value" FROM "FormDefinitions" WHERE "Id" = {DefinitionId}""").SingleAsync(ct));
        Assert.Equal(1L, await db.Database.SqlQuery<long>($"""SELECT "LastRecordNumber" AS "Value" FROM "FormDefinitions" WHERE "Id" = {DefinitionId}""").SingleAsync(ct));
        Assert.Equal("Cuando informa una visita", await db.Database.SqlQuery<string>($"""SELECT "OfferWhen" AS "Value" FROM "FormDefinitions" WHERE "Id" = {DefinitionId}""").SingleAsync(ct));
        Assert.True(await db.Database.SqlQuery<bool>($"""SELECT "SchemaJson" = CAST({SchemaJson} AS jsonb) AS "Value" FROM "BusinessFormVersions" WHERE "Id" = {VersionId}""").SingleAsync(ct));
        Assert.Equal(ActorId, await db.Database.SqlQuery<Guid>($"""SELECT "ActorUserId" AS "Value" FROM "BusinessFormRecords" WHERE "Id" = {RecordId}""").SingleAsync(ct));
        Assert.True(await db.Database.SqlQuery<bool>($"""SELECT "ValuesJson" = CAST({ValuesJson} AS jsonb) AS "Value" FROM "BusinessFormRecords" WHERE "Id" = {RecordId}""").SingleAsync(ct));
        Assert.False(await db.Database.SqlQuery<bool>($"""SELECT "BooleanValue" AS "Value" FROM "BusinessFormRecordValues" WHERE "RecordId" = {RecordId} AND "FieldId" = {FieldId}""").SingleAsync(ct));
        Assert.Equal(RecordId, await db.Database.SqlQuery<Guid>($"""SELECT "RecordId" AS "Value" FROM "BusinessFormCaptureReceipts" WHERE "Id" = {ReceiptId} AND "State" = 'Completed' AND "Number" = 1""").SingleAsync(ct));
        Assert.Equal("Sucursales", await CatalogTextAsync(db, "es", ct));
        Assert.Equal("Branches", await CatalogTextAsync(db, "en", ct));
        Assert.True(await db.Database.SqlQuery<bool>($"""SELECT "IsActive" AND "Revision" = 1 AS "Value" FROM "BusinessFormCatalogs" WHERE "Id" = {CatalogId}""").SingleAsync(ct));
    }

    private Task<string> CatalogTextAsync(ApplicationDbContext db, string culture, CancellationToken ct) => culture == "es"
        ? db.Database.SqlQuery<string>($"""SELECT "DisplayNameEs" AS "Value" FROM "BusinessFormCatalogs" WHERE "Id" = {CatalogId}""").SingleAsync(ct)
        : db.Database.SqlQuery<string>($"""SELECT "DisplayNameEn" AS "Value" FROM "BusinessFormCatalogs" WHERE "Id" = {CatalogId}""").SingleAsync(ct);
}
