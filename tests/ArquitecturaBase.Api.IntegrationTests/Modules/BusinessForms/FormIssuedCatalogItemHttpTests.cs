using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// «Emitir» with a customer written by its tax id, with PostgreSQL and the worker run by hand: the party the issuer completed
// when it queued joins the catalog its standard brings, once, with its fields and a new revision.
public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task A_customer_written_by_its_tax_id_joins_the_catalog_of_its_standard_once_when_it_is_issued()
    {
        using var manager = await OperatorAsync([BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RulesManage]);
        var issuer = new TestRecordIssuer();
        await using var host = IssuerHost(issuer);
        var key = "test.c" + Guid.NewGuid().ToString("N")[..26];
        var customers = await SeedIssuedCustomersAsync(key);
        var (_, firstRun) = await SeedIssueRunAsync(manager);
        var (_, secondRun) = await SeedIssueRunAsync(manager);
        var worker = host.Services.GetRequiredService<FormActionBackgroundService>();
        issuer.Next = new(StructuredIssueState.Pending)
        {
            CatalogItem = new(key, "30500010912", "Agro del Sur S.A.",
                new Dictionary<string, string> { ["tax_id"] = "30500010912", ["vat_condition"] = "6", ["address"] = "Ruta 8 km 225" }, "tax_id"),
        };

        await worker.ProcessNextAsync(firstRun, Ct);
        await worker.ProcessNextAsync(secondRun, Ct);

        Assert.Equal((FormActionRunState.Running, FormActionRunState.Running), ((await RunAsync(firstRun)).State, (await RunAsync(secondRun)).State));
        var (items, revision) = await factory.ExecuteDbContextAsync(async db => (
            await db.Set<BusinessFormCatalogItem>().AsNoTracking().Where(item => item.CatalogId == customers.CatalogId).ToListAsync(Ct),
            await db.Set<BusinessFormCatalog>().Where(catalog => catalog.Id == customers.CatalogId).Select(catalog => catalog.Revision).SingleAsync(Ct)));
        var added = Assert.Single(items);
        Assert.Equal(("30500010912", "Agro del Sur S.A.", true, 2L), (added.Code, added.TextEs, added.IsActive, revision));
        var values = JsonDocument.Parse(added.FieldValuesJson).RootElement;
        Assert.Equal(2, values.EnumerateObject().Count());
        Assert.Equal(("30500010912", customers.Monotributo.ToString("D")), (values.GetProperty(customers.TaxIdField.ToString("D")).GetString(),
            values.GetProperty(customers.ConditionField.ToString("D")).GetString()));
    }

    // A catalog with the key of a standard, its CUIT and its condition, an option of another catalog, as the «Clientes» of the «Factura».
    private Task<(Guid CatalogId, Guid TaxIdField, Guid ConditionField, Guid Monotributo)> SeedIssuedCustomersAsync(string key) =>
        factory.ExecuteDbContextAsync(async db =>
        {
            var suffix = " " + Guid.NewGuid().ToString("N")[..6];
            var conditions = BusinessFormCatalog.CreateManual("es", new Dictionary<string, string> { ["es"] = "Condiciones" + suffix }, new Dictionary<string, string>());
            var monotributo = conditions.AddManualItem("6", "Monotributo", null, null, null, 1).Value;
            var customers = BusinessFormCatalog.CreateManual("es", new Dictionary<string, string> { ["es"] = "Clientes" + suffix }, new Dictionary<string, string>());
            customers.AssignStandardKey(key);
            var taxId = new BusinessFormCatalogField(Guid.CreateVersion7(), "tax_id", new Dictionary<string, string> { ["es"] = "CUIT" }, "text", null, false, false,
                null, null, 13, "data", 0, true);
            var condition = new BusinessFormCatalogField(Guid.CreateVersion7(), "vat_condition", new Dictionary<string, string> { ["es"] = "Condición" }, "catalog",
                conditions.Id, false, false, null, null, null, "data", 1, true);
            Assert.True(customers.SetFields([taxId, condition], null).IsSuccess);
            db.Set<BusinessFormCatalog>().AddRange(conditions, customers);
            db.Set<BusinessFormCatalogItem>().Add(monotributo);
            await db.SaveChangesAsync(Ct);
            return (customers.Id, taxId.Id, condition.Id, monotributo.Id);
        });
}
