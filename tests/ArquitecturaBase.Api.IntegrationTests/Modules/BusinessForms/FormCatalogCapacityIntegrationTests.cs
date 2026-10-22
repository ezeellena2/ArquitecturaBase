using System.Diagnostics;
using System.Globalization;
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
public sealed class FormCatalogCapacityIntegrationTests(ApiFactory factory, ITestOutputHelper output)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_complete_reader_returns_ten_thousand_ordered_items_including_inactive_rows_in_one_query()
    {
        var texts = new Dictionary<string, string> { ["es"] = "Opciones completas", ["en"] = "Complete options" };
        var catalog = BusinessFormCatalog.CreateManual(texts, texts);
        var items = Enumerable.Range(1, 10000).Reverse().Select(position =>
        {
            var number = position.ToString(CultureInfo.InvariantCulture);
            var item = catalog.AddManualItem("CODE-" + number, "Opción " + number, "Option " + number, null, null, position).Value;
            if (position % 2 == 0) Assert.True(catalog.SetManualItemActive(item, false).IsSuccess);
            return item;
        }).ToArray();
        try
        {
            await factory.InTransactionAsync(async services =>
            {
                var repository = services.GetRequiredService<IFormCatalogRepository>();
                await repository.LockAsync(catalog.Id, Ct);
                repository.Add(catalog);
                repository.AddItems(items);
            });
            await factory.ExecuteScopeAsync(async services =>
            {
                var recorder = new FormResponseCommandRecorder();
                await using var db = new TestDbContext(new DbContextOptionsBuilder<ApplicationDbContext>(
                    services.GetRequiredService<DbContextOptions<ApplicationDbContext>>()).AddInterceptors(recorder).Options);
                var elapsed = Stopwatch.StartNew();
                var rows = await new FormCatalogReader(db).ListAllItemsAsync(catalog.Id, Ct);
                elapsed.Stop();
                output.WriteLine("Catalog read: {0:F2} ms; rows={1}; SQL commands={2}.",
                    elapsed.Elapsed.TotalMilliseconds, rows.Count, recorder.Commands.Count);

                Assert.Equal(10000, rows.Count);
                Assert.Equal(5000, rows.Count(item => !item.IsActive));
                Assert.All(rows, item => Assert.Equal(catalog.Id, item.CatalogId));
                Assert.Equal(items.OrderBy(item => item.Position).ThenBy(item => item.Id).Select(item => item.Id), rows.Select(item => item.Id));
                Assert.Equal(Enumerable.Range(1, 10000), rows.Select(item => item.Position));
                Assert.Equal("Opción 10000", rows[^1].TextEs);
                Assert.Equal("Option 10000", rows[^1].TextEn);
                var sql = Assert.Single(recorder.Commands).Sql;
                Assert.Contains("ORDER BY", sql, StringComparison.Ordinal);
                Assert.DoesNotContain("LIMIT", sql, StringComparison.Ordinal);
                Assert.DoesNotContain("OFFSET", sql, StringComparison.Ordinal);
                Assert.Empty(db.ChangeTracker.Entries());
            });
        }
        finally
        {
            await factory.InTransactionAsync(async services =>
            {
                var db = services.GetRequiredService<ApplicationDbContext>();
                db.RemoveRange(await db.Set<BusinessFormCatalogItem>().Where(item => item.CatalogId == catalog.Id).ToArrayAsync(Ct));
                var stored = await db.Set<BusinessFormCatalog>().SingleOrDefaultAsync(value => value.Id == catalog.Id, Ct);
                if (stored is not null) db.Remove(stored);
            });
        }
    }
}
