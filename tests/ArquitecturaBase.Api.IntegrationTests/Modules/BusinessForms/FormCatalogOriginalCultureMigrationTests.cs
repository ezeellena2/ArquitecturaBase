using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

[Collection(ApiTestGroup.Name)]
public sealed class FormCatalogOriginalCultureMigrationTests(ApiFactory factory)
{
    private const string Before = "20261023041344_AddFormPersonData";
    private const string WithOriginalCulture = "AddCatalogAndPersonAttributeOriginalCulture";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Original_culture_comes_from_proposal_evidence_defaults_existing_rows_to_Spanish_and_survives_up_down_up()
    {
        var connection = factory.NewDatabaseConnectionString("catalog_original_culture");
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", connection));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var migrator = db.GetService<IMigrator>();
        try
        {
            await migrator.MigrateAsync(Before, Ct);
            var seed = await InsertAsync(db);
            var history = await HistoryAsync(db);
            await migrator.MigrateAsync(cancellationToken: Ct);
            Assert.False(db.Database.HasPendingModelChanges());
            Assert.Equal(history, await HistoryAsync(db));
            Assert.Equal(("es", "en", "es"), await CulturesAsync(db, seed));

            await migrator.MigrateAsync(Before, Ct);
            Assert.Equal(history, await HistoryAsync(db));
            await migrator.MigrateAsync(cancellationToken: Ct);
            Assert.Equal(history, await HistoryAsync(db));
            Assert.Equal(("es", "en", "es"), await CulturesAsync(db, seed));
        }
        finally { await db.Database.EnsureDeletedAsync(CancellationToken.None); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Down_refuses_an_English_original_outside_a_proposal_before_dropping_the_columns(bool catalog)
    {
        var connection = factory.NewDatabaseConnectionString("catalog_original_culture_down");
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", connection));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var migrator = db.GetService<IMigrator>();
        try
        {
            await migrator.MigrateAsync(Before, Ct);
            var seed = await InsertAsync(db);
            await migrator.MigrateAsync(WithOriginalCulture, Ct);
            if (catalog)
                await db.Database.ExecuteSqlAsync($"""UPDATE "BusinessFormCatalogs" SET "OriginalCulture" = 'en' WHERE "Id" = {seed.CatalogId}""", Ct);
            else
                await db.Database.ExecuteSqlAsync($"""UPDATE "BusinessFormPersonAttributes" SET "OriginalCulture" = 'en' WHERE "Id" = {seed.AttributeId}""", Ct);
            var history = await HistoryAsync(db);
            var applied = await db.Database.GetAppliedMigrationsAsync(Ct);

            var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(Before, Ct));

            Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
            Assert.Equal(applied, await db.Database.GetAppliedMigrationsAsync(Ct));
            Assert.Equal(history, await HistoryAsync(db));
            Assert.Equal(catalog ? ("en", "en", "es") : ("es", "en", "en"), await CulturesAsync(db, seed));
        }
        finally { await db.Database.EnsureDeletedAsync(CancellationToken.None); }
    }

    private async Task<Seed> InsertAsync(ApplicationDbContext db)
    {
        var seed = new Seed(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        const string texts = "{\"es\":\"Sucursales\",\"en\":\"Branches\"}";
        const string proposalName = "{\"en\":\"Team leads\"}";
        const string evidence = "{\"originalCulture\":\"en\",\"turnId\":\"0199f0a1-0000-7000-8000-000000000009\"}";
        const string filters = "{}";
        const string attributeName = "{\"es\":\"Zona\",\"en\":\"Zone\"}";
        var proposalId = Guid.CreateVersion7();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO "BusinessFormCatalogs" ("Id", "Key", "Kind", "NameJson", "DescriptionJson", "IsActive", "Revision", "CreatedAtUtc")
            VALUES ({seed.CatalogId}, 'ORIGINAL-CULTURE', 'Manual', CAST({texts} AS jsonb), CAST({texts} AS jsonb), TRUE, 1, {now});
            INSERT INTO "BusinessFormCatalogs" ("Id", "Key", "Kind", "NameJson", "DescriptionJson", "FiltersJson", "IsActive", "Revision", "CreatedAtUtc",
                "ProposalId", "ProposalEvidenceJson", "ProposalState")
            VALUES ({seed.ProposalCatalogId}, 'ORIGINAL-PROPOSAL', 'Filters', CAST({proposalName} AS jsonb), CAST({proposalName} AS jsonb),
                CAST({filters} AS jsonb), FALSE, 1, {now}, {proposalId}, CAST({evidence} AS jsonb), 'Pending');
            INSERT INTO "BusinessFormPersonAttributes" ("Id", "Key", "NameJson", "Type", "Position", "IsActive", "Revision", "CreatedAtUtc")
            VALUES ({seed.AttributeId}, 'zone', CAST({attributeName} AS jsonb), 'Text', 0, TRUE, 1, {now});
            """, Ct);
        return seed;
    }

    private static Task<string[]> HistoryAsync(ApplicationDbContext db) => db.Database.SqlQueryRaw<string>("""
        SELECT jsonb_build_object('table', 'catalog', 'data', to_jsonb(r) - 'OriginalCulture')::text AS "Value" FROM "BusinessFormCatalogs" r
        UNION ALL SELECT jsonb_build_object('table', 'attribute', 'data', to_jsonb(r) - 'OriginalCulture')::text FROM "BusinessFormPersonAttributes" r
        ORDER BY "Value"
        """).ToArrayAsync(Ct);

    private static async Task<(string Catalog, string Proposal, string Attribute)> CulturesAsync(ApplicationDbContext db, Seed seed) => (
        await db.Database.SqlQuery<string>($"""SELECT "OriginalCulture" AS "Value" FROM "BusinessFormCatalogs" WHERE "Id" = {seed.CatalogId}""").SingleAsync(Ct),
        await db.Database.SqlQuery<string>($"""SELECT "OriginalCulture" AS "Value" FROM "BusinessFormCatalogs" WHERE "Id" = {seed.ProposalCatalogId}""").SingleAsync(Ct),
        await db.Database.SqlQuery<string>($"""SELECT "OriginalCulture" AS "Value" FROM "BusinessFormPersonAttributes" WHERE "Id" = {seed.AttributeId}""").SingleAsync(Ct));

    private sealed record Seed(Guid CatalogId, Guid ProposalCatalogId, Guid AttributeId);
}
