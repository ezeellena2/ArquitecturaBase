using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

[Collection(ApiTestGroup.Name)]
public sealed class FormCatalogActiveCountIntegrationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Active_count_is_one_aggregate_for_its_catalog_without_loading_inactive_or_foreign_items()
    {
        var texts = new Dictionary<string, string> { ["es"] = "Opciones contadas" };
        var catalog = BusinessFormCatalog.CreateManual("es", texts, texts);
        var other = BusinessFormCatalog.CreateManual("es", texts, texts);
        var items = Enumerable.Range(1, 25).Select(position =>
        {
            var item = catalog.AddManualItem("COUNT-" + position, "Opción " + position, null, null, null, position).Value;
            if (position > 20) Assert.True(catalog.SetManualItemActive(item, false).IsSuccess);
            return item;
        }).ToArray();
        var foreign = other.AddManualItem("OTHER", "Otra", null, null, null, 1).Value;
        try
        {
            await factory.InTransactionAsync(async services =>
            {
                var repository = services.GetRequiredService<IFormCatalogRepository>();
                repository.Add(catalog); repository.Add(other); repository.AddItems([.. items, foreign]);
                await Task.CompletedTask;
            });
            await factory.ExecuteScopeAsync(async services =>
            {
                var recorder = new FormResponseCommandRecorder();
                await using var db = new TestDbContext(new DbContextOptionsBuilder<ApplicationDbContext>(
                    services.GetRequiredService<DbContextOptions<ApplicationDbContext>>()).AddInterceptors(recorder).Options);
                var reader = new FormCatalogReader(db);
                Assert.Equal(20, await reader.CountActiveItemsAsync(catalog.Id, Ct));
                var sql = Assert.Single(recorder.Commands).Sql;
                Assert.Contains("count(*)", sql, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("IsActive", sql, StringComparison.Ordinal);
                Assert.DoesNotContain("LIMIT", sql, StringComparison.Ordinal);
                Assert.DoesNotContain("OFFSET", sql, StringComparison.Ordinal);
                Assert.Empty(db.ChangeTracker.Entries());
                Assert.Equal(0, await reader.CountActiveItemsAsync(Guid.CreateVersion7(), Ct));
            });
        }
        finally
        {
            await factory.InTransactionAsync(async services =>
            {
                var db = services.GetRequiredService<ApplicationDbContext>();
                db.RemoveRange(await db.Set<BusinessFormCatalogItem>().Where(item => item.CatalogId == catalog.Id || item.CatalogId == other.Id).ToArrayAsync(Ct));
                db.RemoveRange(await db.Set<BusinessFormCatalog>().Where(value => value.Id == catalog.Id || value.Id == other.Id).ToArrayAsync(Ct));
            });
        }
    }
}
