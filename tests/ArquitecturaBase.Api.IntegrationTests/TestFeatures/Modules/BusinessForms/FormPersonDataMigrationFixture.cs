using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

internal static class FormPersonDataMigrationFixture
{
    public const string Before = "20261023021502_AddCatalogFieldsAndPresentation";
    public const string WithPeople = "AddFormPersonData";

    public static async Task SeedCatalogFieldsAsync(ApplicationDbContext db, FormCatalogValueMigrationFixture seed, CancellationToken ct)
    {
        const string fields = "[{\"id\":\"0199f0a1-0000-7000-8000-000000000001\",\"key\":\"days\",\"name\":{\"es\":\"Días\"},\"type\":\"integer\",\"role\":\"data\",\"position\":0,\"isActive\":true}]";
        const string presentation = "{\"title\":[],\"description\":[]}";
        const string values = "{\"0199f0a1-0000-7000-8000-000000000001\":0}";
        const string snapshot = "[{\"id\":\"0199f0a1-0000-7000-8000-000000000001\",\"key\":\"days\",\"name\":{\"es\":\"Días históricos\"},\"type\":\"integer\",\"role\":\"data\",\"value\":0}]";
        await db.Database.ExecuteSqlAsync($"""
            UPDATE "BusinessFormCatalogs" SET "FieldsJson" = CAST({fields} AS jsonb), "PresentationJson" = CAST({presentation} AS jsonb) WHERE "Id" = {seed.CatalogId};
            UPDATE "BusinessFormCatalogItems" SET "FieldValuesJson" = CAST({values} AS jsonb) WHERE "Id" = {seed.ItemId};
            UPDATE "BusinessFormRecordReferences" SET "FieldValuesJson" = CAST({snapshot} AS jsonb) WHERE "Id" = {seed.ReferenceId};
            """, ct);
    }

    public static Task<string[]> HistoricalRowsAsync(ApplicationDbContext db, CancellationToken ct) => db.Database.SqlQueryRaw<string>("""
        SELECT jsonb_build_object('table', 'definition', 'data', to_jsonb(r))::text AS "Value" FROM "FormDefinitions" r
        UNION ALL SELECT jsonb_build_object('table', 'version', 'data', to_jsonb(r))::text FROM "BusinessFormVersions" r
        UNION ALL SELECT jsonb_build_object('table', 'operation', 'data', to_jsonb(r))::text FROM "BusinessFormPublicationOperations" r
        UNION ALL SELECT jsonb_build_object('table', 'field', 'data', to_jsonb(r))::text FROM "BusinessFormFields" r
        UNION ALL SELECT jsonb_build_object('table', 'record', 'data', to_jsonb(r))::text FROM "BusinessFormRecords" r
        UNION ALL SELECT jsonb_build_object('table', 'recordValue', 'data', to_jsonb(r))::text FROM "BusinessFormRecordValues" r
        UNION ALL SELECT jsonb_build_object('table', 'receipt', 'data', to_jsonb(r))::text FROM "BusinessFormCaptureReceipts" r
        UNION ALL SELECT jsonb_build_object('table', 'catalog', 'data', to_jsonb(r))::text FROM "BusinessFormCatalogs" r
        UNION ALL SELECT jsonb_build_object('table', 'item', 'data', to_jsonb(r))::text FROM "BusinessFormCatalogItems" r
        UNION ALL SELECT jsonb_build_object('table', 'reference', 'data', to_jsonb(r))::text FROM "BusinessFormRecordReferences" r
        ORDER BY "Value"
        """).ToArrayAsync(ct);
}
