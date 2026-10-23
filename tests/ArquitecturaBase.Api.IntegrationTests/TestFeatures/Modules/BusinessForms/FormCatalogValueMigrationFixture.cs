using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

internal sealed record FormCatalogValueMigrationFixture(Guid CatalogId, Guid ItemId, Guid ReferenceId)
{
    public const string Before = "20261022024305_DeferCatalogItemCodeUniqueness";
    public const string WithValueType = "20261022032434_AddCatalogValueType";

    public static async Task<FormCatalogValueMigrationFixture> InsertAsync(ApplicationDbContext db, DateTime nowUtc, CancellationToken ct)
    {
        var definitionId = Guid.CreateVersion7(); var versionId = Guid.CreateVersion7(); var fieldId = Guid.CreateVersion7();
        var recordId = Guid.CreateVersion7(); var catalogId = Guid.CreateVersion7(); var itemId = Guid.CreateVersion7();
        var referenceId = Guid.CreateVersion7(); var receiptId = Guid.CreateVersion7(); var sourceReceiptId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7(); var valueId = Guid.CreateVersion7();
        var labels = new Dictionary<string, string> { ["es"] = "Sucursal", ["en"] = "Branch" };
        var field = new CanonicalFormField(fieldId, "field_" + fieldId.ToString("N"), "choice", labels, null, true, null, null, new(catalogId));
        var draft = JsonSerializer.Serialize(new FormQuestionSchema(1, [field]), JsonSerializerOptions.Web);
        var schema = JsonSerializer.Serialize(new FormVersionSchema(1, [field]), JsonSerializerOptions.Web);
        var values = JsonSerializer.Serialize(new[] { new { FieldId = fieldId, Value = itemId } }, JsonSerializerOptions.Web);
        const string name = """{"es":"Visita histórica","en":"Historical visit"}""";
        const string submit = """{"es":"Enviar","en":"Submit"}""";
        const string catalogName = """{"es":"Sucursales","en":"Branches"}""";
        const string translations = """{"es":{"text":"Norte","detail":"Zona norte","catalogName":"Sucursales"},"en":{"text":"North","detail":"Northern area","catalogName":"Branches"}}""";
        var fingerprint = new string('a', 64);
        // Insert only columns available before AddCatalogValueType; sealed JSON and receipts stay untouched.
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO "FormDefinitions" ("Id", "Name", "NameJson", "Prefix", "Culture", "SubmitLabelJson", "SchemaJson",
                "Revision", "QuestionCount", "PrefixLocked", "LastRecordNumber", "CreatedAtUtc", "OfferWhen")
            VALUES ({definitionId}, 'Visita histórica', CAST({name} AS jsonb), 'VALUEHIST', 'es', CAST({submit} AS jsonb),
                CAST({draft} AS jsonb), 1, 1, TRUE, 1, {nowUtc}, 'Cuando registra una visita');
            INSERT INTO "BusinessFormVersions" ("Id", "DefinitionId", "Number", "ContentHash", "SchemaFormatVersion",
                "SchemaJson", "NameJson", "SubmitLabelJson", "Culture", "QuestionCount", "CreatedAtUtc", "PublishedCultures")
            VALUES ({versionId}, {definitionId}, 1, {fingerprint}, 1, CAST({schema} AS jsonb), CAST({name} AS jsonb),
                CAST({submit} AS jsonb), 'es', 1, {nowUtc}, ARRAY['es','en']);
            UPDATE "FormDefinitions" SET "ActiveVersionId" = {versionId} WHERE "Id" = {definitionId};
            INSERT INTO "BusinessFormFields" ("Id", "DefinitionId", "StableKey", "Type")
            VALUES ({fieldId}, {definitionId}, {field.Key}, 'choice');
            INSERT INTO "BusinessFormRecords" ("Id", "DefinitionId", "VersionId", "Number", "ActorUserId", "SourceReceiptId",
                "SubmittedAtUtc", "ValuesJson", "CreatedAtUtc", "State")
            VALUES ({recordId}, {definitionId}, {versionId}, 1, {actorId}, {sourceReceiptId}, {nowUtc}, CAST({values} AS jsonb), {nowUtc}, 'Received');
            INSERT INTO "BusinessFormRecordValues" ("Id", "DefinitionId", "RecordId", "FieldId", "Type", "ChoiceId")
            VALUES ({valueId}, {definitionId}, {recordId}, {fieldId}, 'choice', {itemId});
            INSERT INTO "BusinessFormCaptureReceipts" ("Id", "SourceReceiptId", "DefinitionId", "VersionId", "Fingerprint",
                "RecordId", "Number", "State", "CreatedAtUtc")
            VALUES ({receiptId}, {sourceReceiptId}, {definitionId}, {versionId}, {fingerprint}, {recordId}, 1, 'Completed', {nowUtc});
            INSERT INTO "BusinessFormCatalogs" ("Id", "Key", "Kind", "NameJson", "DescriptionJson", "IsActive", "Revision", "CreatedAtUtc")
            VALUES ({catalogId}, 'VALUEHIST', 'Manual', CAST({catalogName} AS jsonb), CAST({catalogName} AS jsonb), TRUE, 1, {nowUtc});
            INSERT INTO "BusinessFormCatalogItems" ("Id", "CatalogId", "Code", "TextEs", "TextEn", "DetailEs", "DetailEn", "Position", "IsActive", "CreatedAtUtc")
            VALUES ({itemId}, {catalogId}, 'NORTH', 'Norte', 'North', 'Zona norte', 'Northern area', 1, TRUE, {nowUtc});
            INSERT INTO "BusinessFormRecordReferences" ("Id", "RecordId", "DefinitionId", "FieldId", "CatalogId", "CatalogRevision",
                "ItemId", "Text", "Detail", "CatalogName", "TranslationsJson")
            VALUES ({referenceId}, {recordId}, {definitionId}, {fieldId}, {catalogId}, 1, {itemId}, 'Norte', 'Zona norte', 'Sucursales', CAST({translations} AS jsonb));
            """, ct);
        return new(catalogId, itemId, referenceId);
    }

    public static Task<string[]> HistoricalRowsAsync(ApplicationDbContext db, CancellationToken ct) => db.Database.SqlQueryRaw<string>("""
        SELECT jsonb_build_object('table', 'definition', 'data', to_jsonb(r))::text AS "Value" FROM "FormDefinitions" r
        UNION ALL SELECT jsonb_build_object('table', 'version', 'data', to_jsonb(r))::text FROM "BusinessFormVersions" r
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
            'DisplayDescriptionEs', r."DisplayDescriptionEs", 'DisplayDescriptionEn', r."DisplayDescriptionEn"))::text FROM "BusinessFormCatalogs" r
        UNION ALL SELECT jsonb_build_object('table', 'item', 'data', jsonb_build_object(
            'Id', r."Id", 'CatalogId', r."CatalogId", 'Code', r."Code", 'TextEs', r."TextEs", 'TextEn', r."TextEn",
            'DetailEs', r."DetailEs", 'DetailEn', r."DetailEn", 'Position', r."Position", 'IsActive', r."IsActive",
            'CreatedAtUtc', r."CreatedAtUtc", 'CreatedBy', r."CreatedBy", 'ModifiedAtUtc', r."ModifiedAtUtc", 'ModifiedBy', r."ModifiedBy"))::text
            FROM "BusinessFormCatalogItems" r
        UNION ALL SELECT jsonb_build_object('table', 'reference', 'data', jsonb_build_object(
            'Id', r."Id", 'RecordId', r."RecordId", 'DefinitionId', r."DefinitionId", 'FieldId', r."FieldId", 'CatalogId', r."CatalogId",
            'CatalogRevision', r."CatalogRevision", 'ItemId', r."ItemId", 'Text', r."Text", 'Detail', r."Detail",
            'CatalogName', r."CatalogName", 'TranslationsJson', r."TranslationsJson"))::text FROM "BusinessFormRecordReferences" r
        ORDER BY "Value"
        """).ToArrayAsync(ct);

    public static async Task<Guid> CreateOperatorAsync(IServiceProvider services, CancellationToken ct)
    {
        var result = await services.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(async token =>
        {
            var roles = services.GetRequiredService<IRoleRepository>();
            // The isolated migrated database has no seed; user creation assigns this initial role.
            await roles.CreateAsync(SystemRoles.User, null, [], token);
            var roleName = "typed-catalog-" + Guid.NewGuid().ToString("N");
            await roles.CreateAsync(roleName, null, [BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage], token);
            var users = services.GetRequiredService<IUserRepository>();
            var user = await users.CreateAsync(Email.Create(TestEmails.Unique("typed-catalog")).Value, null, false, null, "es", token);
            await users.SetRolesAsync(user.Id, [roleName], token);
            return Result.Success(user.Id);
        }, CommitPolicy.OnSuccess, ct);
        return result.Value;
    }
}
