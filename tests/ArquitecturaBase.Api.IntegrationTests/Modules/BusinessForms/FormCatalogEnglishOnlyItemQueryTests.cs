using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
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
public sealed class FormCatalogEnglishOnlyItemQueryTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_option_without_Spanish_text_is_searched_sorted_and_shown_with_its_English_original_in_Spanish()
    {
        var connection = factory.NewDatabaseConnectionString("catalog_english_only_item");
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", connection));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        try
        {
            await db.GetService<IMigrator>().MigrateAsync(cancellationToken: Ct);
            var source = BusinessFormCatalog.CreateManual("en", new Dictionary<string, string> { ["en"] = "Branches" }, new Dictionary<string, string>());
            var alpha = source.AddManualItem("ALPHA", null, "Alpha branch", null, "Main office", 1).Value;
            var beta = source.AddManualItem("BETA", "Beta sucursal", "Beta branch", null, null, 2).Value;
            var visits = BusinessFormCatalog.CreateManual("es", new Dictionary<string, string> { ["es"] = "Visitas" }, new Dictionary<string, string>());
            var field = new BusinessFormCatalogField(Guid.CreateVersion7(), "branch", new Dictionary<string, string> { ["es"] = "Sucursal" },
                "catalog", source.Id, false, false, null, null, null, "data", 0, true);
            Assert.True(visits.SetFields([field], null).IsSuccess);
            var visit = visits.AddManualItem("VISIT", "Visita", null, null, null, 1).Value;
            Assert.True(visits.SetManualItemFields(visit, JsonSerializer.Serialize(new Dictionary<string, string> { [field.Id.ToString("D")] = alpha.Id.ToString("D") })).IsSuccess);
            var attribute = BusinessFormPersonAttribute.Create("branch", "es", new Dictionary<string, string> { ["es"] = "Sucursal" },
                BusinessFormPersonAttributeType.Catalog, source.Id, 0).Value;
            var person = ApplicationUser.Create("english-only-option@arquitecturabase.test", false, null, false, "English option", "es");
            var values = JsonSerializer.Serialize(new Dictionary<string, string> { [attribute.Id.ToString("D")] = alpha.Id.ToString("D") });
            db.AddRange(source, alpha, beta, visits, visit, attribute, person, BusinessFormPersonData.Create(person.Id, values));
            await db.SaveChangesAsync(Ct);
            db.ChangeTracker.Clear();
            using var culture = new CultureScope("es-AR");
            var catalogs = new FormCatalogReader(db);
            var people = new FormPersonDataReader(db);

            var found = Assert.Single((await catalogs.ListItemsAsync(source.Id, new() { DisplayCulture = "es", Search = "Alpha" }, Ct)).Items);
            var sorted = await catalogs.ListItemsAsync(source.Id, new() { DisplayCulture = "es", Sort = "text" }, Ct);
            var withSource = Assert.Single((await catalogs.ListItemsAsync(visits.Id, new() { DisplayCulture = "es" }, Ct)).Items);
            var option = Assert.Single((await people.ListOptionsAsync(source.Id, new() { DisplayCulture = "es", Search = "Alpha" }, Ct)).Items);
            var options = await people.ListOptionsAsync(source.Id, new() { DisplayCulture = "es" }, Ct);
            var recipient = await people.FindRecipientDataAsync(person.Id, Ct);

            Assert.Equal(alpha.Id, found.Id);
            Assert.Null(found.Text["es"]);
            Assert.Equal("Alpha branch", found.Text["en"]);
            Assert.Equal([alpha.Id, beta.Id], sorted.Items.Select(item => item.Id));
            var texts = withSource.FieldTexts[field.Id.ToString("D")];
            Assert.Equal(["en"], texts.Keys);
            Assert.Equal("Alpha branch", texts["en"]);
            Assert.Equal((alpha.Id, "Alpha branch"), (option.Id, option.Text));
            Assert.Equal(["Alpha branch", "Beta sucursal"], options.Items.Select(item => item.Text));
            Assert.Equal("Alpha branch", recipient.OptionTexts[alpha.Id].Text);
        }
        finally { await db.Database.EnsureDeletedAsync(CancellationToken.None); }
    }
}
