using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

[Collection(ApiTestGroup.Name)]
public sealed class FormExternalCatalogsMigrationTests(ApiFactory factory)
{
    private const string Before = "20261110064200_AddWhatsAppAssistantAttachmentMessage";
    private const string External = "20261111134134_AddExternalCatalogs";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Down_refuses_an_external_catalog_before_dropping_its_columns()
    {
        var connection = factory.NewDatabaseConnectionString("catalog_external_down");
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", connection));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var migrator = db.GetService<IMigrator>();
        try
        {
            await migrator.MigrateAsync(External, Ct);
            var now = factory.Clock.GetUtcNow().UtcDateTime;
            const string name = "{\"es\":\"Conceptos\"}";
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO "BusinessFormCatalogs" ("Id", "Key", "Kind", "ValueType", "NameJson", "DescriptionJson", "OriginalCulture", "IsActive", "Revision", "CreatedAtUtc", "FeedRefreshedAtUtc")
                VALUES ({Guid.CreateVersion7()}, 'arca.concepts', 'External', 'Integer', CAST({name} AS jsonb), CAST({name} AS jsonb), 'es', TRUE, 1, {now}, {now});
                """, Ct);
            var applied = await db.Database.GetAppliedMigrationsAsync(Ct);

            var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(Before, Ct));

            Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
            Assert.Equal(applied, await db.Database.GetAppliedMigrationsAsync(Ct));
            Assert.Equal(4, await FeedColumnsAsync(db));
        }
        finally { await db.Database.EnsureDeletedAsync(CancellationToken.None); }
    }

    private static Task<int> FeedColumnsAsync(ApplicationDbContext db) => db.Database.SqlQueryRaw<int>("""
        SELECT COUNT(*)::int AS "Value" FROM information_schema.columns
        WHERE table_name = 'BusinessFormCatalogs' AND column_name LIKE 'Feed%'
        """).SingleAsync(Ct);
}
