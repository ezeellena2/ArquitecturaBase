using System.Globalization;
using System.Security.Claims;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.Arca;
using ArquitecturaBase.Application.Interfaces.Channels;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.Arca.Registry;

/// <summary>
/// «Completar con ARCA» por el puerto neutral, con PostgreSQL y <c>FakeArca</c>: una CUIT inválida no llega a ARCA; sin conexión
/// dice por qué; con conexión, la constancia trae el nombre (también entero en «Razón social»), la condición frente al IVA del
/// receptor y el domicilio, y una CUIT que
/// el padrón no tiene responde que no la encontró. En delegación la consulta la plataforma con su CUIT, sin que la empresa delegue
/// la constancia. Una CUIT que ya respondió sale de la caché y una falla se vuelve a consultar; cada persona tiene un cupo de CUIT
/// distintas por minuto, y repetir una no lo gasta.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class ArcaStructuredPartyLookupTests(ApiFactory factory) : IDisposable
{
    private readonly FakeArcaCertificateAuthority authority = new();

    [Fact]
    public async Task The_registry_completes_a_customer_from_its_cuit()
    {
        var arca = factory.Services.GetRequiredService<FakeArca>();
        arca.Reset();

        Assert.Equal("Arca.Registry.CuitInvalid", (await FindAsync("30500010913")).FailureCode);
        Assert.Equal("Arca.Connection.NotConnected", (await FindAsync("30500010912")).FailureCode);

        await ArcaVoucherScenario.ConnectAsync(factory, authority);
        try
        {
            // The whole legal name also goes in its own field: the text of an option of «Clientes» admits 30 characters.
            const string legalName = "Cooperativa Agrícola Ganadera y de Consumo Limitada del Sur de Córdoba";
            arca.Person = new FakeArcaPerson(legalName, null, null, "Ruta 9 km 400", "Marcos Juárez", "CORDOBA", [30], false, []);
            var found = await FindAsync("30-50001091-2");
            Assert.Equal(legalName, found.Name);
            Assert.Equal(("30500010912", legalName, "1"), (found.Fields!["tax_id"], found.Fields["legal_name"], found.Fields["vat_condition"]));
            Assert.Contains("Ruta 9 km 400", found.Fields["address"], StringComparison.Ordinal);

            arca.Person = null;
            Assert.Equal("Arca.Registry.PersonNotFound", (await FindAsync("20123456786")).FailureCode);
        }
        finally
        {
            arca.Person = FakeArca.Company;
            await ArcaVoucherScenario.RemoveAsync(factory);
        }
    }

    [Fact]
    public async Task In_delegation_the_platform_completes_a_customer_with_its_own_cuit()
    {
        await using var scenario = await ArcaHttpScenario.CreateAsync(factory, authority);
        // La empresa todavía no delegó Facturación: el ticket sólo representa a la plataforma, y a la constancia le alcanza.
        scenario.Arca.Relations = [ArcaPersistenceScenario.PlatformCuit];
        await scenario.StartAsync([FakeArca.InvoicingService], mode: "PlatformDelegation");
        await scenario.UploadAsync();
        scenario.Arca.Person = new FakeArcaPerson("Agro del Sur SA", null, null, "Ruta 9 km 400", "Marcos Juárez", "CORDOBA", [30], false, []);

        var found = await FindAsync("30-50001091-2");

        Assert.Equal(("Agro del Sur SA", null), (found.Name, found.FailureCode));
        var consulted = scenario.Arca.LastRequest("getPersona_v2")!;
        Assert.Equal((ArcaPersistenceScenario.PlatformCuit, "30500010912"), (consulted.Element("cuitRepresentada")?.Value, consulted.Element("idPersona")?.Value));
    }

    [Fact]
    public async Task A_cuit_that_answered_comes_from_the_cache_and_a_failure_is_asked_again()
    {
        var arca = factory.Services.GetRequiredService<FakeArca>();
        arca.Reset();
        await ArcaVoucherScenario.ConnectAsync(factory, authority);
        try
        {
            // Another CUIT first, so the ticket of the constancia is ready and the failure falls on the registry.
            arca.Person = new FakeArcaPerson("Agro del Sur SA", null, null, "Ruta 9 km 400", "Marcos Juárez", "CORDOBA", [30], false, []);
            Assert.Null((await FindAsync("20-12345678-6")).FailureCode);
            arca.Fail(FakeArca.RegistryService, FakeArcaFailure.ServiceUnavailable);
            Assert.Equal("Arca.Connection.Unavailable", (await FindAsync("30-50001091-2")).FailureCode);

            Assert.Equal("Agro del Sur SA", (await FindAsync("30500010912")).Name);
            var calls = arca.Calls("getPersona_v2", FakeArca.RegistryService);
            arca.Person = new FakeArcaPerson("Otra razón social SA", null, null, null, null, null, [30], false, []);

            Assert.Equal("Agro del Sur SA", (await FindAsync("30-50001091-2")).Name);
            Assert.Equal(calls, arca.Calls("getPersona_v2", FakeArca.RegistryService));
        }
        finally
        {
            arca.Person = FakeArca.Company;
            await ArcaVoucherScenario.RemoveAsync(factory);
        }
    }

    [Fact]
    public async Task Each_person_looks_up_a_few_new_cuits_a_minute_and_repeating_one_does_not_count()
    {
        // Without a connection the lookups that pass the quota stop at «not connected», before ARCA.
        var operatorId = Guid.NewGuid();
        string[] cuits = ["20000000028", "20000000036", "20000000044", "20000000052", "20000000060", "20000000079"];
        foreach (var cuit in cuits) Assert.Equal("Arca.Connection.NotConnected", (await FindAsync(cuit, operatorId)).FailureCode);
        Assert.Equal("Arca.Connection.NotConnected", (await FindAsync(cuits[0], operatorId)).FailureCode);

        Assert.Equal("Arca.Registry.TooManyLookups", (await FindAsync("20000000087", operatorId)).FailureCode);
        Assert.Equal("Arca.Connection.NotConnected", (await FindAsync("20000000087", Guid.NewGuid())).FailureCode);
        // What runs without a person, like the issuance in the background, has no quota.
        Assert.Equal("Arca.Connection.NotConnected", (await FindAsync("20000000087")).FailureCode);
    }

    public void Dispose() => authority.Dispose();

    // With an operator, as a request of that person would ask it; without one, as the work in the background.
    private async Task<Application.Models.StructuredData.StructuredPartyLookupResponse> FindAsync(string taxId, Guid? operatorId = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = operatorId is { } id
            ? new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", id.ToString("D", CultureInfo.InvariantCulture))], "test")) }
            : null;
        try
        {
            return await scope.ServiceProvider.GetServices<IStructuredPartyLookup>().Single(lookup => lookup.CatalogKey == "arca.customers")
                .FindAsync(taxId, TestContext.Current.CancellationToken);
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }
}
