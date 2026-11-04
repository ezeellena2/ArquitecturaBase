using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.Arca;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Registry;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Registry;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.Arca.Registry;

/// <summary><c>getPersona_v2</c> de la constancia de inscripción contra FakeArca, con los nombres del WSDL de homologación.</summary>
[Collection(ApiTestGroup.Name)]
public sealed class ArcaRegistryClientTests(ApiFactory factory)
{
    private static readonly ArcaCertificateRef Certificate = new(Guid.Empty, Guid.Empty, "20000000060", ArcaEnvironment.Testing, "", "");
    private static readonly ArcaTicketOutcome.Ready Ticket = new("the-token", "the-sign", DateTime.MaxValue);

    [Fact]
    public async Task A_company_comes_with_its_name_address_and_taxes_and_the_request_carries_the_auth()
    {
        var arca = Arca();

        var reply = await Client().FindPersonAsync(Certificate, Ticket, "30000000007", TestContext.Current.CancellationToken);

        Assert.Equal(ArcaServiceCheckStatus.Ready, reply.Status);
        var person = reply.Value!;
        Assert.Equal(("Campo SA", null, null), (person.LegalName, person.FirstName, person.LastName));
        Assert.Equal(new ArcaRegistryAddress("Ruta 9 km 400", "Marcos Juárez", "CORDOBA", "2580"), person.FiscalAddress);
        Assert.Equal([30], person.TaxIds);
        Assert.False(person.IsMonotax);
        Assert.Empty(person.Errors);
        var request = arca.LastRequest("getPersona_v2")!;
        Assert.Equal(("the-token", "the-sign", "20000000060", "30000000007"),
            (request.Element("token")?.Value, request.Element("sign")?.Value, request.Element("cuitRepresentada")?.Value, request.Element("idPersona")?.Value));
    }

    [Fact]
    public async Task A_person_comes_with_first_and_last_name_and_monotax()
    {
        Arca().Person = new FakeArcaPerson(null, "Juana", "Pérez", "San Martín 10", "Rosario", "SANTA FE", [], true, []);

        var person = (await Client().FindPersonAsync(Certificate, Ticket, "27000000005", TestContext.Current.CancellationToken)).Value!;

        Assert.Equal((null, "Juana", "Pérez"), (person.LegalName, person.FirstName, person.LastName));
        Assert.True(person.IsMonotax);
        Assert.Empty(person.TaxIds);
    }

    [Fact]
    public async Task A_constancia_error_is_returned_without_an_exception()
    {
        Arca().Person = FakeArca.Company with { ConstanciaErrors = ["El domicilio fiscal no está validado"] };

        var reply = await Client().FindPersonAsync(Certificate, Ticket, "30000000007", TestContext.Current.CancellationToken);

        Assert.Equal(ArcaServiceCheckStatus.Ready, reply.Status);
        Assert.Equal(["El domicilio fiscal no está validado"], reply.Value!.Errors);
    }

    [Fact]
    public async Task A_cuit_that_does_not_exist_fails()
    {
        Arca().Person = null;

        var reply = await Client().FindPersonAsync(Certificate, Ticket, "30000000007", TestContext.Current.CancellationToken);

        Assert.Equal((ArcaServiceCheckStatus.Failed, "registry.fault"), (reply.Status, reply.FailureCode));
    }

    private FakeArca Arca()
    {
        var arca = factory.Services.GetRequiredService<FakeArca>();
        arca.Reset();
        return arca;
    }

    private IArcaRegistryClient Client() => factory.Services.GetRequiredService<IArcaRegistryClient>();
}
