using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

[Collection(ApiTestGroup.Name)]
public sealed class FormAssistantMigrationTests(ApiFactory factory)
{
    [Fact]
    public async Task Assistant_columns_preserve_existing_form_record_receipt_and_bilingual_catalog_in_both_directions()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb",
            factory.NewDatabaseConnectionString("form_assistant_migration")));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var migrator = db.GetService<IMigrator>();
        try
        {
            await migrator.MigrateAsync(FormAssistantMigrationFixture.Before, ct);
            var original = await FormAssistantMigrationFixture.InsertAsync(db, factory.Clock.GetUtcNow().UtcDateTime, ct);
            await migrator.MigrateAsync(cancellationToken: ct);
            Assert.False(db.Database.HasPendingModelChanges());
            Assert.Empty(await db.Database.GetPendingMigrationsAsync(ct));
            await original.AssertUnchangedAsync(db, ct);
            var record = await db.Set<BusinessFormRecord>().AsNoTracking().SingleAsync(ct);
            Assert.Null(record.CaptureOriginJson); Assert.Null(record.OriginRecordId); Assert.Null(record.OriginSuggestionId);
            var definition = await db.Set<BusinessFormDefinition>().AsNoTracking().SingleAsync(ct);
            Assert.Null(definition.ActiveSuggestionSetId);
            var catalog = await db.Set<BusinessFormCatalog>().AsNoTracking().SingleAsync(ct);
            Assert.Null(catalog.ProposalId); Assert.Null(catalog.ProposalEvidenceJson); Assert.Null(catalog.ProposalState);
            await migrator.MigrateAsync(FormAssistantMigrationFixture.Before, ct);
            await original.AssertUnchangedAsync(db, ct);
            await migrator.MigrateAsync(cancellationToken: ct);
            await original.AssertUnchangedAsync(db, ct);
        }
        finally { await db.Database.EnsureDeletedAsync(ct); }
    }
}
