using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Repositories;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

[Collection(ApiTestGroup.Name)]
public sealed class FormCatalogFieldsCapacityIntegrationTests(ApiFactory factory, ITestOutputHelper output)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Ten_thousand_options_with_twenty_fields_use_typed_containment_before_the_offer_limit_and_one_capture_batch()
    {
        var texts = new Dictionary<string, string> { ["es"] = "Campos completos", ["en"] = "Complete fields" };
        var catalog = BusinessFormCatalog.CreateManual(texts, texts);
        var fields = Enumerable.Range(0, 20).Select(index => new BusinessFormCatalogField(Guid.NewGuid(), "field_" + index,
            texts, index switch { 0 => "integer", 1 => "boolean", 2 => "decimal", _ => "text" }, null,
            false, false, null, null, index > 2 ? 300 : null, index < 2 ? "filter" : "data", index, true)).ToArray();
        Assert.True(catalog.SetFields(fields, null).IsSuccess);
        var items = Enumerable.Range(1, 10000).Select(position =>
        {
            var number = position.ToString(CultureInfo.InvariantCulture);
            var item = catalog.AddManualItem("ITEM-" + number, "Opción " + number, "Option " + number, null, null, position).Value;
            var values = fields.Select((field, index) => (field.Id, Value: index switch
            {
                0 => (object)(position % 100), 1 => position % 2 == 0, 2 => "1.5", _ => "Value " + number,
            })).ToDictionary(value => value.Id.ToString("D"), value => value.Value);
            Assert.True(catalog.SetManualItemFields(item, JsonSerializer.Serialize(values)).IsSuccess);
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
                await db.Database.ExecuteSqlRawAsync("ANALYZE \"BusinessFormCatalogItems\"", Ct);
                var reader = new FormCatalogReader(db);
                var containment = JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    [fields[0].Id.ToString("D")] = 4, [fields[1].Id.ToString("D")] = true,
                    [fields[2].Id.ToString("D")] = "1.5",
                });
                var elapsed = Stopwatch.StartNew();
                var offered = await reader.ListOfferItemsAsync(catalog.Id, Ct, containment);
                elapsed.Stop();
                var offerMilliseconds = elapsed.Elapsed.TotalMilliseconds;
                Assert.Equal(100, offered.Count);
                Assert.Equal(Enumerable.Range(0, 100).Select(index => index * 100 + 4), offered.Select(item => item.Position));
                var generated = Assert.Single(recorder.Commands);
                Assert.Contains("@>", generated.Sql, StringComparison.Ordinal);
                Assert.Contains("LIMIT", generated.Sql, StringComparison.Ordinal);

                elapsed.Restart();
                var captured = await new FormRecordRepository(db).FindCatalogFieldValuesAsync(offered.Select(item => item.Id).ToArray(), Ct);
                elapsed.Stop();
                var captureMilliseconds = elapsed.Elapsed.TotalMilliseconds;
                Assert.Equal(100, captured.Count);
                Assert.Equal(2, recorder.Commands.Count);
                Assert.All(captured, row => Assert.Equal(20, JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row.FieldValuesJson)!.Count));
                Assert.Empty(db.ChangeTracker.Entries());
                var differentJsonType = JsonSerializer.Serialize(new Dictionary<string, string> { [fields[0].Id.ToString("D")] = "4" });
                Assert.Empty(await reader.ListOfferItemsAsync(catalog.Id, Ct, differentJsonType));
                Assert.Equal(201, (await reader.ListOfferItemsAsync(catalog.Id, Ct)).Count);

                await db.Database.OpenConnectionAsync(Ct);
                await using var command = db.Database.GetDbConnection().CreateCommand();
                command.CommandText = "EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + generated.Sql;
                foreach (var parameter in generated.Parameters) command.Parameters.Add(parameter);
                var plan = (string)(await command.ExecuteScalarAsync(Ct))!;
                using var document = JsonDocument.Parse(plan);
                Assert.Equal(100d, document.RootElement[0].GetProperty("Plan").GetProperty("Actual Rows").GetDouble());
                output.WriteLine("Catalog 10000x20: offered={0}; containment={1:F2} ms; selected batch={2:F2} ms.", offered.Count, offerMilliseconds, captureMilliseconds);
                var destination = Environment.GetEnvironmentVariable("FORM_CATALOG_FIELDS_PERFORMANCE_OUTPUT");
                if (!string.IsNullOrWhiteSpace(destination))
                {
                    Directory.CreateDirectory(destination);
                    await File.WriteAllTextAsync(Path.Combine(destination, "catalog-fields-10000x20.json"),
                        JsonSerializer.Serialize(new { rows = 10000, fields = 20, offered = offered.Count, offerMilliseconds, captureMilliseconds,
                            plan = document.RootElement.Clone() }), Ct);
                }
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
