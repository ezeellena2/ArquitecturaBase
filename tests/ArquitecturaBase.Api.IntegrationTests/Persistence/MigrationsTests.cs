using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using InfrastructureSetup = ArquitecturaBase.Infrastructure.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Persistence;

/// <summary>
/// Usan el ApplicationDbContext de producción armado con las opciones registradas. El TestDbContext del arnés suma
/// la tabla de Widgets, que no es parte de las migraciones.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class MigrationsTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Model_has_no_pending_changes()
    {
        var pending = await factory.ExecuteScopeAsync(services =>
        {
            using var dbContext = new ApplicationDbContext(services.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
            return Task.FromResult(dbContext.Database.HasPendingModelChanges());
        });

        Assert.False(pending);
    }

    [Fact]
    public async Task Migrations_create_the_schema_on_an_empty_database()
    {
        await using var emptyDatabase = factory.WithWebHostBuilder(builder => builder.UseSetting(
            $"ConnectionStrings:{InfrastructureSetup.DatabaseConnectionName}", factory.NewDatabaseConnectionString("migrations")));
        await using var scope = emptyDatabase.Services.CreateAsyncScope();
        await using var dbContext = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());

        try
        {
            await dbContext.Database.MigrateAsync(Ct);

            Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync(Ct));
            Assert.False(await dbContext.LoginCodes.AnyAsync(Ct));
            Assert.False(await dbContext.Users.AnyAsync(Ct));
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync(Ct);
        }
    }

    [Fact]
    public async Task Retired_identity_migration_preserves_existing_json_ids_in_both_directions()
    {
        const string previousMigration = "20261010171711_AddFormVersionPresentationAndContactLinkRevision";
        var definitionId = Guid.CreateVersion7();
        var fieldId = Guid.CreateVersion7();
        var choiceId = Guid.CreateVersion7();
        await using var emptyDatabase = factory.WithWebHostBuilder(builder => builder.UseSetting(
            $"ConnectionStrings:{InfrastructureSetup.DatabaseConnectionName}", factory.NewDatabaseConnectionString("retiredids")));
        await using var scope = emptyDatabase.Services.CreateAsyncScope();
        await using var dbContext = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var migrator = dbContext.GetService<IMigrator>();

        try
        {
            await migrator.MigrateAsync(previousMigration, Ct);
            await dbContext.Database.ExecuteSqlRawAsync("""
                INSERT INTO "FormDefinitions"
                    ("Id", "Name", "NameJson", "Prefix", "PrefixLocked", "Revision", "Culture", "SubmitLabelJson",
                     "SchemaJson", "RetiredIdentityLedgerJson", "QuestionCount", "CreatedAtUtc")
                VALUES ({0}, 'Migration test', '{{"es":"Migration test"}}'::jsonb, 'MIGTEST', false, 1, 'es',
                    '{{"es":"Enviar"}}'::jsonb, '{{"format":1,"fields":[]}}'::jsonb,
                    jsonb_build_object('fields', jsonb_build_array({1}::text), 'choices', jsonb_build_array({2}::text)), 0, now())
                """, [definitionId, fieldId, choiceId], Ct);

            await dbContext.Database.MigrateAsync(Ct);

            var migratedCount = await dbContext.Database.SqlQueryRaw<long>("""
                SELECT count(*) AS "Value" FROM "BusinessFormRetiredIdentities" WHERE "DefinitionId" = {0}
                """, definitionId).SingleAsync(Ct);
            Assert.Equal(2, migratedCount);

            await migrator.MigrateAsync(previousMigration, Ct);
            var restoredLedger = await dbContext.Database.SqlQueryRaw<string>("""
                SELECT "RetiredIdentityLedgerJson"::text AS "Value" FROM "FormDefinitions" WHERE "Id" = {0}
                """, definitionId).SingleAsync(Ct);
            using var document = System.Text.Json.JsonDocument.Parse(restoredLedger);
            Assert.Equal(fieldId.ToString(), document.RootElement.GetProperty("fields")[0].GetString());
            Assert.Equal(choiceId.ToString(), document.RootElement.GetProperty("choices")[0].GetString());
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync(Ct);
        }
    }

    [Fact]
    public async Task Flow_session_migration_can_be_applied_and_rolled_back_after_retired_identity_migration()
    {
        const string previousMigration = "20261011030121_AddFormRetiredIdentities";
        const string flowMigration = "20261011041238_AddWhatsAppFlowSending";
        await using var emptyDatabase = factory.WithWebHostBuilder(builder => builder.UseSetting(
            $"ConnectionStrings:{InfrastructureSetup.DatabaseConnectionName}", factory.NewDatabaseConnectionString("flowsessions")));
        await using var scope = emptyDatabase.Services.CreateAsyncScope();
        await using var dbContext = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var migrator = dbContext.GetService<IMigrator>();

        try
        {
            await migrator.MigrateAsync(previousMigration, Ct);
            await migrator.MigrateAsync(flowMigration, Ct);
            Assert.True(await dbContext.Database.SqlQueryRaw<bool>("SELECT to_regclass('public.\"WhatsAppFlowSessions\"') IS NOT NULL AS \"Value\"").SingleAsync(Ct));

            await migrator.MigrateAsync(previousMigration, Ct);
            Assert.False(await dbContext.Database.SqlQueryRaw<bool>("SELECT to_regclass('public.\"WhatsAppFlowSessions\"') IS NOT NULL AS \"Value\"").SingleAsync(Ct));
            var remainingSessionColumn = await dbContext.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS (SELECT 1 FROM information_schema.columns
                    WHERE table_name = 'WhatsAppOutgoingMessages' AND column_name = 'FlowSessionId') AS "Value"
                """).SingleAsync(Ct);
            Assert.False(remainingSessionColumn);
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync(Ct);
        }
    }
}
