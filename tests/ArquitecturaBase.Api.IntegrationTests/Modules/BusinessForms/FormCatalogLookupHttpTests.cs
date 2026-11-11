using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// «Completar con ARCA» (CA1): the catalog a registry completes, by its Key, says so and fills an option from a tax id, without saving.
public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task A_catalog_a_registry_completes_fills_an_option_from_its_tax_id_and_others_do_not_offer_it()
    {
        using var designer = await OperatorAsync(["formtemplates.read", "formtemplates.manage", BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage]);
        using var reader = await OperatorAsync([BusinessFormsPermissions.CatalogsRead]);
        using var copied = await designer.PostAsJsonAsync($"/api/forms/library/{TestFormStandardSource.CustomersKey}/copies",
            Add(Guid.NewGuid(), "V" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()), Ct);
        Assert.Equal(HttpStatusCode.Created, copied.StatusCode);
        var catalogId = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormCatalog>()
            .Where(catalog => catalog.Key == TestFormStandardSource.CustomersCatalog).Select(catalog => catalog.Id).SingleAsync(Ct));
        var lookup = new TestPartyLookup();
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IStructuredPartyLookup>();
            services.AddSingleton<IStructuredPartyLookup>(lookup);
        }));
        using var managing = InHost(host, designer);
        using var reading = InHost(host, reader);

        using var offered = await managing.GetAsync($"/api/forms/catalogs/{catalogId}/lookup", Ct);
        Assert.Equal(HttpStatusCode.OK, offered.StatusCode);
        var offer = await offered.ReadJsonAsync();
        Assert.Equal("Padrón de prueba", offer.GetProperty("name").GetProperty("es").GetString());
        var cuitField = offer.GetProperty("fieldId").GetGuid();

        lookup.Next = new("Agro del Sur S.A.", new Dictionary<string, string> { ["cuit"] = "30500010912", ["unknown"] = "x" });
        using var found = await managing.PostAsJsonAsync($"/api/forms/catalogs/{catalogId}/lookup", new { taxId = " 30-50001091-2 " }, Ct);
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        var party = await found.ReadJsonAsync();
        Assert.Equal("Agro del Sur S.A.", party.GetProperty("text").GetString());
        var value = Assert.Single(party.GetProperty("fields").EnumerateArray());
        Assert.Equal((cuitField, "30500010912"), (value.GetProperty("fieldId").GetGuid(), value.GetProperty("value").GetString()));
        Assert.Equal("30-50001091-2", Assert.Single(lookup.Asked));

        lookup.Next = new(null, null, "Arca.Registry.PersonNotFound");
        using var unknown = await managing.PostAsJsonAsync($"/api/forms/catalogs/{catalogId}/lookup", new { taxId = "20123456786" }, Ct);
        var failure = await unknown.ReadJsonAsync();
        Assert.Equal("Arca.Registry.PersonNotFound", failure.GetProperty("failureCode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(failure.GetProperty("failureMessage").GetString()));

        using var empty = await managing.PostAsJsonAsync($"/api/forms/catalogs/{catalogId}/lookup", new { taxId = " " }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        using var forbidden = await reading.PostAsJsonAsync($"/api/forms/catalogs/{catalogId}/lookup", new { taxId = "20123456786" }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        var other = await SeedCatalogAsync("Sin padrón");
        using var none = await managing.GetAsync($"/api/forms/catalogs/{other}/lookup", Ct);
        Assert.Equal((HttpStatusCode.NotFound, FormCatalogErrors.LookupUnavailable.Code), (none.StatusCode, (await none.ReadJsonAsync()).GetProperty("code").GetString()));
        Assert.Single(lookup.Asked.Skip(1));
    }

    private async Task<Guid> SeedCatalogAsync(string name)
    {
        var catalog = BusinessFormCatalog.CreateManual("es", new Dictionary<string, string> { ["es"] = name + " " + Guid.NewGuid().ToString("N")[..6] },
            new Dictionary<string, string>());
        await factory.ExecuteDbContextAsync(async db =>
        {
            db.Set<BusinessFormCatalog>().Add(catalog);
            await db.SaveChangesAsync(Ct);
        });
        return catalog.Id;
    }
}
