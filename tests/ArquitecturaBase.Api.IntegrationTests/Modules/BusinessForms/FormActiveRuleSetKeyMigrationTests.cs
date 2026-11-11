using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// DeferActiveRuleSetKey on a migrated database (EnsureCreated does not create the key): the active rule set still belongs to its
// own definition, checked at commit, so «Agregar» saves a new definition and its first rule set together, and a failure in the
// middle of the save leaves nothing.
[Collection(ApiTestGroup.Name)]
public sealed class FormActiveRuleSetKeyMigrationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_active_rule_set_key_is_checked_at_commit_keeps_the_rows_and_goes_back_to_immediate()
    {
        var connection = factory.NewDatabaseConnectionString("active_rule_set_key");
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", connection));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var migrator = db.GetService<IMigrator>();
        try
        {
            await migrator.MigrateAsync(FormActiveRuleSetMigrationFixture.Before, Ct);
            var nowUtc = factory.Clock.GetUtcNow().UtcDateTime;
            var first = await FormActiveRuleSetMigrationFixture.InsertAsync(db, "KEYA", nowUtc, Ct);
            var second = await FormActiveRuleSetMigrationFixture.InsertAsync(db, "KEYB", nowUtc, Ct);
            Assert.Equal((true, false), await FormActiveRuleSetMigrationFixture.KeyAsync(db, Ct));

            await migrator.MigrateAsync(cancellationToken: Ct);
            Assert.Equal((true, true), await FormActiveRuleSetMigrationFixture.KeyAsync(db, Ct));
            Assert.False(await FormActiveRuleSetMigrationFixture.IndexExistsAsync(db, Ct));
            Assert.Equal(first.RuleSetId, await ActiveRuleSetAsync(db, first.DefinitionId));
            Assert.False(db.Database.HasPendingModelChanges());

            // A rule set of another form, or one that does not exist, is accepted by the statement and refused at commit.
            foreach (var foreign in new[] { second.RuleSetId, Guid.CreateVersion7() })
            {
                await using var transaction = await db.Database.BeginTransactionAsync(Ct);
                await db.Database.ExecuteSqlAsync($"""UPDATE "FormDefinitions" SET "ActiveRuleSetId" = {foreign} WHERE "Id" = {first.DefinitionId}""", Ct);
                var error = await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync(Ct));
                Assert.Equal((PostgresErrorCodes.ForeignKeyViolation, FormActiveRuleSetMigrationFixture.Key), (error.SqlState, error.ConstraintName));
            }
            Assert.Equal(first.RuleSetId, await ActiveRuleSetAsync(db, first.DefinitionId));

            await migrator.MigrateAsync(FormActiveRuleSetMigrationFixture.Before, Ct);
            Assert.Equal((true, false), await FormActiveRuleSetMigrationFixture.KeyAsync(db, Ct));
            Assert.True(await FormActiveRuleSetMigrationFixture.IndexExistsAsync(db, Ct));
            var immediate = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync(
                $"""UPDATE "FormDefinitions" SET "ActiveRuleSetId" = {second.RuleSetId} WHERE "Id" = {first.DefinitionId}""", Ct));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, immediate.SqlState);
            await migrator.MigrateAsync(cancellationToken: Ct);
            Assert.Equal((true, true), await FormActiveRuleSetMigrationFixture.KeyAsync(db, Ct));
        }
        finally
        {
            await db.Database.EnsureDeletedAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_standard_with_rules_is_added_in_one_commit_and_a_failure_in_the_middle_leaves_nothing()
    {
        var connection = factory.NewDatabaseConnectionString("standard_rules");
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", connection)
            .ConfigureTestServices(services =>
            {
                services.RemoveAll<IStructuredRecordIssuer>();
                services.AddSingleton<IStructuredRecordIssuer>(new TestRecordIssuer());
            }));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        try
        {
            await db.GetService<IMigrator>().MigrateAsync(cancellationToken: Ct);
            var designer = await FormActiveRuleSetMigrationFixture.CreateDesignerAsync(scope.ServiceProvider, Ct);
            using var client = host.CreateClient();
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, designer.ToString());

            // The rules go in before the folder: a folder that is gone undoes the copy, its catalog and its rules together.
            using var gone = await client.PostAsJsonAsync($"/api/forms/library/{TestRecordIssuer.Key}/copies",
                new { requestId = Guid.NewGuid(), name = new { es = "Comprobante propio" }, prefix = "CPPA", folderId = Guid.NewGuid() }, Ct);
            Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
            Assert.False(await db.Set<BusinessFormDefinition>().AnyAsync(Ct));
            Assert.False(await db.Set<BusinessFormRuleSet>().AnyAsync(Ct));
            Assert.False(await db.Set<BusinessFormCatalog>().AnyAsync(catalog => catalog.Key == TestFormStandardSource.PrioritiesCatalog, Ct));

            using var added = await client.PostAsJsonAsync($"/api/forms/library/{TestRecordIssuer.Key}/copies",
                new { requestId = Guid.NewGuid(), name = new { es = "Comprobante propio" }, prefix = "CPPA" }, Ct);
            Assert.Equal(HttpStatusCode.Created, added.StatusCode);
            var id = (await added.ReadJsonAsync()).GetProperty("definition").GetProperty("id").GetGuid();
            var ruleSet = await db.Set<BusinessFormRuleSet>().AsNoTracking().SingleAsync(Ct);
            Assert.Equal((id, 1L), (ruleSet.DefinitionId, ruleSet.Revision));
            Assert.Equal(ruleSet.Id, await ActiveRuleSetAsync(db, id));
            using var rules = await client.GetAsync($"/api/forms/definitions/{id}/rules", Ct);
            var body = await rules.ReadJsonAsync();
            Assert.Equal((1L, 2), (body.GetProperty("revision").GetInt64(), body.GetProperty("rules").GetArrayLength()));
        }
        finally
        {
            await db.Database.EnsureDeletedAsync(CancellationToken.None);
        }
    }

    private static Task<Guid?> ActiveRuleSetAsync(ApplicationDbContext db, Guid definitionId) =>
        db.Set<BusinessFormDefinition>().AsNoTracking().Where(definition => definition.Id == definitionId).Select(definition => definition.ActiveRuleSetId).SingleAsync(Ct);
}
