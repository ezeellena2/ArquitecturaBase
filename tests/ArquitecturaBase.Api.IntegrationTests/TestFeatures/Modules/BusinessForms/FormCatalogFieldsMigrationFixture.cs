using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

internal static class FormCatalogFieldsMigrationFixture
{
    public const string Before = "20261022125008_AddFormMultipleChoiceValues";
    public const string WithFields = "AddCatalogFieldsAndPresentation";

    public static Task<string[]> HistoricalRowsAsync(ApplicationDbContext db, CancellationToken ct) => db.Database.SqlQueryRaw<string>("""
        SELECT jsonb_build_object('table', 'definition', 'data', to_jsonb(r))::text AS "Value" FROM "FormDefinitions" r
        UNION ALL SELECT jsonb_build_object('table', 'version', 'data', to_jsonb(r))::text FROM "BusinessFormVersions" r
        UNION ALL SELECT jsonb_build_object('table', 'operation', 'data', to_jsonb(r))::text FROM "BusinessFormPublicationOperations" r
        UNION ALL SELECT jsonb_build_object('table', 'field', 'data', to_jsonb(r))::text FROM "BusinessFormFields" r
        UNION ALL SELECT jsonb_build_object('table', 'record', 'data', to_jsonb(r))::text FROM "BusinessFormRecords" r
        UNION ALL SELECT jsonb_build_object('table', 'recordValue', 'data', to_jsonb(r))::text FROM "BusinessFormRecordValues" r
        UNION ALL SELECT jsonb_build_object('table', 'receipt', 'data', to_jsonb(r))::text FROM "BusinessFormCaptureReceipts" r
        UNION ALL SELECT jsonb_build_object('table', 'catalog', 'data', to_jsonb(r) - ARRAY['FieldsJson','PresentationJson'])::text FROM "BusinessFormCatalogs" r
        UNION ALL SELECT jsonb_build_object('table', 'item', 'data', to_jsonb(r) - 'FieldValuesJson')::text FROM "BusinessFormCatalogItems" r
        UNION ALL SELECT jsonb_build_object('table', 'reference', 'data', to_jsonb(r) - 'FieldValuesJson')::text FROM "BusinessFormRecordReferences" r
        ORDER BY "Value"
        """).ToArrayAsync(ct);
}
