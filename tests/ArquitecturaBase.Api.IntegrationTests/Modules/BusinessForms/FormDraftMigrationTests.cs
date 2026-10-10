using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using InfrastructureSetup = ArquitecturaBase.Infrastructure.DependencyInjection;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;
[Collection(ApiTestGroup.Name)]
public sealed class FormDraftMigrationTests(ApiFactory factory)
{
    [Fact]
    public async Task Initial_form_migration_updates_the_previous_schema_and_round_trips_both_name_texts()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting(
            $"ConnectionStrings:{InfrastructureSetup.DatabaseConnectionName}", factory.NewDatabaseConnectionString("forms_migration")));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        try
        {
            var migrations = db.Database.GetMigrations().ToArray();
            var index = Array.FindIndex(migrations, migration => migration.EndsWith("_AddBusinessFormDrafts", StringComparison.Ordinal));
            Assert.True(index > 0);
            await db.GetService<IMigrator>().MigrateAsync(migrations[index - 1], ct);
            await db.Database.ExecuteSqlRawAsync("INSERT INTO \"DataProtectionKeys\" (\"FriendlyName\", \"Xml\") VALUES ('forms-migration-proof', '<key />')", ct);
            await db.Database.MigrateAsync(ct);
            Assert.Empty(await db.Database.GetPendingMigrationsAsync(ct));
            Assert.False(db.Database.HasPendingModelChanges());
            Assert.True(await db.DataProtectionKeys.AnyAsync(key => key.FriendlyName == "forms-migration-proof", ct));
            var definition = BusinessFormDefinition.Create();
            Assert.True(definition.SetDraft(1, "Nombre", "{\"en\":\"Name\",\"es\":\"Nombre\"}", "FORM", "es",
                "{\"en\":\"Submit\",\"es\":\"Enviar\"}", "{\"format\":1,\"fields\":[]}", "{\"fields\":[],\"choices\":[]}", 0, true).IsSuccess);
            db.Add(definition); await db.SaveChangesAsync(ct); db.ChangeTracker.Clear();
            var loaded = await db.Set<BusinessFormDefinition>().SingleAsync(ct);
            Assert.Equal("Nombre", loaded.Name); Assert.Contains("\"en\": \"Name\"", loaded.NameJson, StringComparison.Ordinal);
            await db.GetService<IMigrator>().MigrateAsync(migrations[index - 1], ct);
            Assert.True(await db.DataProtectionKeys.AnyAsync(key => key.FriendlyName == "forms-migration-proof", ct));
        }
        finally { await db.Database.EnsureDeletedAsync(ct); }
    }
}