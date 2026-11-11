using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.Arca;
using ArquitecturaBase.Application.Interfaces.Channels;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.Arca.Registry;

/// <summary>
/// «Completar con ARCA» por el puerto neutral, con PostgreSQL y <c>FakeArca</c>: una CUIT inválida no llega a ARCA; sin conexión
/// dice por qué; con conexión, la constancia trae el nombre, la condición frente al IVA del receptor y el domicilio, y una CUIT que
/// el padrón no tiene responde que no la encontró.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class ArcaStructuredPartyLookupTests(ApiFactory factory) : IDisposable
{
    private readonly FakeArcaCertificateAuthority authority = new();

    [Fact]
    public async Task The_registry_completes_a_customer_from_its_cuit()
    {
        var ct = TestContext.Current.CancellationToken;
        var arca = factory.Services.GetRequiredService<FakeArca>();
        arca.Reset();

        Assert.Equal("Arca.Registry.CuitInvalid", (await FindAsync("30500010913")).FailureCode);
        Assert.Equal("Arca.Connection.NotConnected", (await FindAsync("30500010912")).FailureCode);

        await ArcaVoucherScenario.ConnectAsync(factory, authority);
        try
        {
            arca.Person = new FakeArcaPerson("Agro del Sur SA", null, null, "Ruta 9 km 400", "Marcos Juárez", "CORDOBA", [30], false, []);
            var found = await FindAsync("30-50001091-2");
            Assert.Equal("Agro del Sur SA", found.Name);
            Assert.Equal(("30500010912", "1"), (found.Fields!["tax_id"], found.Fields["vat_condition"]));
            Assert.Contains("Ruta 9 km 400", found.Fields["address"], StringComparison.Ordinal);

            arca.Person = null;
            Assert.Equal("Arca.Registry.PersonNotFound", (await FindAsync("20123456786")).FailureCode);
        }
        finally
        {
            arca.Person = FakeArca.Company;
            await ArcaVoucherScenario.RemoveAsync(factory);
        }

        async Task<Application.Models.StructuredData.StructuredPartyLookupResponse> FindAsync(string taxId)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetServices<IStructuredPartyLookup>().Single(lookup => lookup.CatalogKey == "arca.customers").FindAsync(taxId, ct);
        }
    }

    public void Dispose() => authority.Dispose();
}
