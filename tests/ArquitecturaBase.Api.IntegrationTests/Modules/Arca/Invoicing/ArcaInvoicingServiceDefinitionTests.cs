using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.Arca;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Channels.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.Arca.Invoicing;

/// <summary>La familia de facturación presentada a la conexión: su prueba pide el ticket y llama a FEDummy.</summary>
[Collection(ApiTestGroup.Name)]
public sealed class ArcaInvoicingServiceDefinitionTests(ApiFactory factory) : IDisposable
{
    private readonly FakeArcaCertificateAuthority authority = new();

    [Fact]
    public async Task The_check_is_ready_when_the_ticket_comes_and_the_server_answers_ok()
    {
        var arca = Arca();
        var certificate = await ArcaPersistenceScenario.AddActiveCertificateAsync(factory, authority);
        try
        {
            var check = await CheckAsync(certificate);

            Assert.Equal((FakeArca.InvoicingService, ArcaServiceCheckStatus.Ready), (check.Service, check.Status));
            Assert.NotNull(check.TicketExpiresAtUtc);
            Assert.Equal((1, 1), (arca.Calls("loginCms", FakeArca.InvoicingService), arca.Calls("FEDummy", FakeArca.InvoicingService)));
        }
        finally
        {
            await ArcaPersistenceScenario.RemoveConnectionAsync(factory);
        }
    }

    [Fact]
    public async Task A_server_that_does_not_answer_ok_is_unavailable()
    {
        Arca().ServerStatus = ("OK", "OK", "NO");
        var certificate = await ArcaPersistenceScenario.AddActiveCertificateAsync(factory, authority);
        try
        {
            Assert.Equal(ArcaServiceCheckStatus.Unavailable, (await CheckAsync(certificate)).Status);
        }
        finally
        {
            await ArcaPersistenceScenario.RemoveConnectionAsync(factory);
        }
    }

    [Fact]
    public async Task A_blocked_ticket_is_reported_with_its_retry_time_without_calling_the_service()
    {
        var arca = Arca();
        arca.Fail(FakeArca.InvoicingService, FakeArcaFailure.AlreadyAuthenticated);
        var certificate = await ArcaPersistenceScenario.AddActiveCertificateAsync(factory, authority);
        try
        {
            var check = await CheckAsync(certificate);

            Assert.Equal(ArcaServiceCheckStatus.Blocked, check.Status);
            Assert.NotNull(check.RetryAfterUtc);
            Assert.Equal(0, arca.Calls("FEDummy", FakeArca.InvoicingService));
        }
        finally
        {
            await ArcaPersistenceScenario.RemoveConnectionAsync(factory);
        }
    }

    [Fact]
    public async Task The_family_is_optional_and_complete_once_a_point_of_sale_is_chosen()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionId = await ArcaPersistenceScenario.AddConnectionAsync(factory);
        try
        {
            await factory.ExecuteScopeAsync(async services =>
            {
                var definition = Definition(services);
                Assert.Equal([new ArcaInstalledService(ArcaPersistenceScenario.Invoicing, "Invoicing", false)], definition.Services);
                Assert.False(await definition.IsCompleteAsync(connectionId, ct));
            });

            await factory.InTransactionAsync(services =>
            {
                var settings = ArcaInvoicingSettings.Create(connectionId);
                Assert.True(settings.ChoosePointOfSale(3).IsSuccess);
                services.GetRequiredService<IArcaInvoicingRepository>().Add(settings);
                return Task.CompletedTask;
            });

            Assert.True(await factory.ExecuteScopeAsync(services => Definition(services).IsCompleteAsync(connectionId, ct)));
        }
        finally
        {
            await ArcaPersistenceScenario.RemoveConnectionAsync(factory);
        }
    }

    public void Dispose() => authority.Dispose();

    private static IArcaServiceDefinition Definition(IServiceProvider services) => services.GetServices<IArcaServiceDefinition>()
        .Single(definition => definition.Services.Any(service => service.Code == ArcaPersistenceScenario.Invoicing));

    private FakeArca Arca()
    {
        var arca = factory.Services.GetRequiredService<FakeArca>();
        arca.Reset();
        return arca;
    }

    private Task<ArcaServiceCheckResponse> CheckAsync(ArcaCertificateRef certificate) => factory.ExecuteScopeAsync(services =>
        Definition(services).CheckAsync(ArcaPersistenceScenario.Invoicing,
            new ArcaServiceContext(certificate, services.GetRequiredService<IArcaAccessTicketProvider>()), TestContext.Current.CancellationToken));
}
