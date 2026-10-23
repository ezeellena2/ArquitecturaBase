using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Identity;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

[Collection(ApiTestGroup.Name)]
public sealed class FormPersonDataPersistenceTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Person_reader_pages_all_non_deleted_people_and_loads_option_texts_without_discarding_inactive_data()
    {
        var connection = factory.NewDatabaseConnectionString("person_data_read");
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", connection));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        try
        {
            await db.GetService<IMigrator>().MigrateAsync(cancellationToken: Ct);
            var seed = await FormCatalogValueMigrationFixture.InsertAsync(db, factory.Clock.GetUtcNow().UtcDateTime, Ct);
            var names = new Dictionary<string, string> { ["es"] = "Zona", ["en"] = "Area" };
            var attribute = BusinessFormPersonAttribute.Create("zone", "es", names, BusinessFormPersonAttributeType.Catalog, seed.CatalogId, 0).Value;
            var inactive = BusinessFormPersonAttribute.Create("notes", "es", names, BusinessFormPersonAttributeType.Text, null, 1).Value;
            Assert.True(inactive.Update(1, names, BusinessFormPersonAttributeType.Text, null, 1, false).IsSuccess);
            var active = ApplicationUser.Create("person-active@arquitecturabase.test", false, null, false, "Pilot person", "es");
            var disabled = ApplicationUser.Create("person-disabled@arquitecturabase.test", false, null, false, "Pilot person", "es");
            disabled.SetActive(false);
            var deleted = ApplicationUser.Create("person-deleted@arquitecturabase.test", false, null, false, "Pilot deleted", "es");
            var unnamed = ApplicationUser.Create("private@arquitecturabase.test", false, null, false, null, "es");
            var values = JsonSerializer.Serialize(new Dictionary<string, string> { [attribute.Id.ToString("D")] = seed.ItemId.ToString("D"), [inactive.Id.ToString("D")] = "Kept note" });
            db.AddRange(attribute, inactive, active, disabled, deleted, unnamed, BusinessFormPersonData.Create(active.Id, values));
            await db.SaveChangesAsync(Ct);
            await db.Database.ExecuteSqlAsync($"""UPDATE "AspNetUsers" SET "IsDeleted" = TRUE WHERE "Id" = {deleted.Id}""", Ct);
            db.ChangeTracker.Clear();
            var reader = new FormPersonDataReader(db);
            var page = await reader.ListPeopleAsync(new() { Search = "Pilot", PageSize = 1, Page = 1, DisplayCulture = "en" }, Ct);
            var second = await reader.ListPeopleAsync(new() { Search = "Pilot", PageSize = 1, Page = 2, DisplayCulture = "en" }, Ct);
            Assert.Equal(2, page.TotalCount);
            Assert.Equal(2, second.TotalCount);
            Assert.Equal(new[] { active.Id, disabled.Id }.Order(), page.Items.Concat(second.Items).Select(value => value.UserId).Order());
            var current = Assert.Single(page.Items.Concat(second.Items), value => value.UserId == active.Id);
            Assert.Equal("North", current.OptionTexts[seed.ItemId]);
            using var keptValues = JsonDocument.Parse(current.ValuesJson);
            Assert.Equal("Kept note", keptValues.RootElement.GetProperty(inactive.Id.ToString("D")).GetString());
            var blank = await reader.FindRecipientDataAsync(disabled.Id, Ct);
            Assert.Null(blank.Revision);
            Assert.Equal("{}", blank.ValuesJson);
            Assert.Equal(2, blank.Attributes.Count);
            Assert.Contains(blank.Attributes, value => value.Id == inactive.Id && !value.IsActive);
            var option = Assert.Single((await reader.ListOptionsAsync(seed.CatalogId, new() { DisplayCulture = "en", Search = "North" }, Ct)).Items);
            Assert.Equal(seed.ItemId, option.Id);
            Assert.Equal("North", option.Text);
            var catalogs = new FormCatalogReader(db);
            Assert.True(await catalogs.ExistsSourceUsageAsync(seed.CatalogId, Ct));
            await db.Database.ExecuteSqlAsync($"""UPDATE "BusinessFormPersonAttributes" SET "IsActive" = FALSE WHERE "Id" = {attribute.Id}""", Ct);
            Assert.False(await catalogs.ExistsSourceUsageAsync(seed.CatalogId, Ct));
            await db.Database.ExecuteSqlAsync($"""UPDATE "BusinessFormCatalogItems" SET "IsActive" = FALSE WHERE "Id" = {seed.ItemId}""", Ct);
            Assert.Empty((await reader.ListOptionsAsync(seed.CatalogId, new(), Ct)).Items);
            var retained = await reader.FindRecipientDataAsync(active.Id, Ct);
            Assert.Contains(seed.ItemId, retained.OptionTexts.Keys);
            var all = await reader.ListPeopleAsync(new() { PageSize = 100 }, Ct);
            Assert.Null(Assert.Single(all.Items, value => value.UserId == unnamed.Id).DisplayName);
            Assert.Empty(db.ChangeTracker.Entries());
        }
        finally { await db.Database.EnsureDeletedAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task Option_texts_leave_out_an_item_of_another_manual_catalog_than_the_attribute_source()
    {
        var connection = factory.NewDatabaseConnectionString("person_data_source");
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", connection));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        try
        {
            await db.GetService<IMigrator>().MigrateAsync(cancellationToken: Ct);
            var zones = BusinessFormCatalog.CreateManual("es", new Dictionary<string, string> { ["es"] = "Zonas" }, new Dictionary<string, string>());
            var regions = BusinessFormCatalog.CreateManual("es", new Dictionary<string, string> { ["es"] = "Regiones" }, new Dictionary<string, string>());
            var north = zones.AddManualItem("NORTE", "Norte", null, null, null, 1).Value;
            var south = regions.AddManualItem("SUR", "Sur", null, null, null, 1).Value;
            var attribute = BusinessFormPersonAttribute.Create("zone", "es", new Dictionary<string, string> { ["es"] = "Zona" },
                BusinessFormPersonAttributeType.Catalog, zones.Id, 0).Value;
            var person = ApplicationUser.Create("person-source@arquitecturabase.test", false, null, false, "Source person", "es");
            var values = JsonSerializer.Serialize(new Dictionary<string, string> { [attribute.Id.ToString("D")] = south.Id.ToString("D") });
            db.AddRange(zones, regions, north, south, attribute, person, BusinessFormPersonData.Create(person.Id, values));
            await db.SaveChangesAsync(Ct);
            db.ChangeTracker.Clear();
            var reader = new FormPersonDataReader(db);
            var data = await reader.FindRecipientDataAsync(person.Id, Ct);
            using (var stored = JsonDocument.Parse(data.ValuesJson))
                Assert.Equal(south.Id.ToString("D"), stored.RootElement.GetProperty(attribute.Id.ToString("D")).GetString());
            Assert.DoesNotContain(south.Id, data.OptionTexts.Keys);
            var row = Assert.Single((await reader.ListPeopleAsync(new() { Search = "Source person" }, Ct)).Items);
            Assert.DoesNotContain(south.Id, row.OptionTexts.Keys);
        }
        finally { await db.Database.EnsureDeletedAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task Attribute_usage_deduplicates_draft_and_active_questions_and_keeps_deactivated_forms()
    {
        var connection = factory.NewDatabaseConnectionString("person_data_usage");
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", connection));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        try
        {
            await db.GetService<IMigrator>().MigrateAsync(cancellationToken: Ct);
            await FormCatalogValueMigrationFixture.InsertAsync(db, factory.Clock.GetUtcNow().UtcDateTime, Ct);
            var first = Guid.NewGuid(); var second = Guid.NewGuid(); var questionId = Guid.NewGuid();
            object Question(Guid attributeId) => new { fieldId = questionId, source = new { match = new[] { new { fieldId = questionId, personAttributeId = attributeId } } } };
            var draft = JsonSerializer.Serialize(new { fields = new[] { Question(first), Question(first), Question(second) } });
            var active = JsonSerializer.Serialize(new { questions = new[] { Question(first) } });
            var now = factory.Clock.GetUtcNow().UtcDateTime; var actor = Guid.NewGuid();
            await db.Database.ExecuteSqlAsync($"""
                UPDATE "FormDefinitions" SET "SchemaJson" = CAST({draft} AS jsonb), "DeactivatedAtUtc" = {now}, "DeactivatedBy" = {actor};
                UPDATE "BusinessFormVersions" SET "SchemaJson" = CAST({active} AS jsonb);
                """, Ct);
            var reader = new FormPersonDataReader(db);
            var usage = await reader.ListAttributeUsagesAsync([first, second], Ct);
            Assert.Equal(2, usage.Count);
            Assert.Single(usage, value => value.AttributeId == first);
            Assert.Single(usage, value => value.AttributeId == second);
            Assert.Single(usage.Select(value => value.DefinitionId).Distinct());
            await db.Database.ExecuteSqlAsync($"""UPDATE "FormDefinitions" SET "IsDeleted" = TRUE""", Ct);
            Assert.Empty(await reader.ListAttributeUsagesAsync([first, second], Ct));
        }
        finally { await db.Database.EnsureDeletedAsync(CancellationToken.None); }
    }
}
