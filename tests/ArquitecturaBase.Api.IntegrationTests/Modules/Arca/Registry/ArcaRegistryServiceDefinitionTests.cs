using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.Arca;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Channels.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Registry;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Registry;
using ArquitecturaBase.Domain.Modules.Arca.Registry;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.Arca.Registry;

/// <summary>La familia del padrón presentada a la conexión: su prueba pide el ticket, consulta la CUIT y guarda la empresa.</summary>
[Collection(ApiTestGroup.Name)]
public sealed class ArcaRegistryServiceDefinitionTests(ApiFactory factory) : IDisposable
{
    private readonly FakeArcaCertificateAuthority authority = new();

    [Fact]
    public async Task The_family_declares_the_registry_as_required_and_is_always_complete()
    {
        await factory.ExecuteScopeAsync(async services =>
        {
            var definition = Definition(services);
            Assert.Equal([new ArcaInstalledService(ArcaPersistenceScenario.Registry, "Registry", true)], definition.Services);
            Assert.True(await definition.IsCompleteAsync(Guid.CreateVersion7(), TestContext.Current.CancellationToken));
        });
    }

    [Fact]
    public async Task The_check_saves_the_company_and_a_later_check_updates_it()
    {
        var arca = Arca();
        var certificate = await ArcaPersistenceScenario.AddActiveCertificateAsync(factory, authority);
        try
        {
            var check = await CheckAsync(certificate);
            Assert.Equal((FakeArca.RegistryService, ArcaServiceCheckStatus.Ready), (check.Service, check.Status));
            Assert.NotNull(check.TicketExpiresAtUtc);
            Assert.Equal(new ArcaRegistryCompanyRow("Campo SA", ArcaVatCondition.RegisteredTaxpayer, "Ruta 9 km 400, Marcos Juárez, CORDOBA"),
                await FindCompanyAsync(certificate));

            arca.Person = FakeArca.Company with { TaxIds = [], Monotax = true };
            await CheckAsync(certificate);
            Assert.Equal(ArcaVatCondition.Monotax, (await FindCompanyAsync(certificate))?.VatCondition);
        }
        finally
        {
            await ArcaPersistenceScenario.RemoveConnectionAsync(factory);
        }
    }

    [Fact]
    public async Task A_ticket_that_is_not_authorized_is_reported_and_saves_nothing()
    {
        Arca().Fail(FakeArca.RegistryService, FakeArcaFailure.NotAuthorized);
        var certificate = await ArcaPersistenceScenario.AddActiveCertificateAsync(factory, authority);
        try
        {
            Assert.Equal(ArcaServiceCheckStatus.NotAuthorized, (await CheckAsync(certificate)).Status);
            Assert.Null(await FindCompanyAsync(certificate));
        }
        finally
        {
            await ArcaPersistenceScenario.RemoveConnectionAsync(factory);
        }
    }

    [Fact]
    public async Task A_person_without_a_name_fails_and_saves_nothing()
    {
        Arca().Person = new FakeArcaPerson(null, null, null, null, null, null, [], false, ["La CUIT no tiene datos"]);
        var certificate = await ArcaPersistenceScenario.AddActiveCertificateAsync(factory, authority);
        try
        {
            var check = await CheckAsync(certificate);
            Assert.Equal((ArcaServiceCheckStatus.Failed, "registry.withoutData"), (check.Status, check.FailureCode));
            Assert.Null(await FindCompanyAsync(certificate));
        }
        finally
        {
            await ArcaPersistenceScenario.RemoveConnectionAsync(factory);
        }
    }

    public void Dispose() => authority.Dispose();

    private static IArcaServiceDefinition Definition(IServiceProvider services) => services.GetServices<IArcaServiceDefinition>()
        .Single(definition => definition.Services.Any(service => service.Code == ArcaPersistenceScenario.Registry));

    private FakeArca Arca()
    {
        var arca = factory.Services.GetRequiredService<FakeArca>();
        arca.Reset();
        return arca;
    }

    private Task<ArcaServiceCheckResponse> CheckAsync(ArcaCertificateRef certificate) => factory.ExecuteScopeAsync(services =>
        Definition(services).CheckAsync(ArcaPersistenceScenario.Registry,
            new ArcaServiceContext(certificate, services.GetRequiredService<IArcaAccessTicketProvider>()), TestContext.Current.CancellationToken));

    private Task<ArcaRegistryCompanyRow?> FindCompanyAsync(ArcaCertificateRef certificate) => factory.ExecuteScopeAsync(services =>
        services.GetRequiredService<IArcaRegistryReader>().FindCompanyAsync(certificate.ConnectionId, TestContext.Current.CancellationToken));
}
