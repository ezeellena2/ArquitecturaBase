using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.Arca;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.Arca.Connection;

/// <summary>
/// H15: the saves that call ARCA first confirm, under the lock, that the connection is still the one they read: the test, if
/// its services changed meanwhile, and the point of sale, if the connection was replaced while ARCA was asked.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class ArcaConnectionConfirmTests(ApiFactory factory) : IDisposable
{
    private readonly FakeArcaCertificateAuthority authority = new();

    [Fact]
    public async Task A_test_whose_services_changed_meanwhile_is_a_conflict_and_records_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var arca = Arca();
        await ArcaPersistenceScenario.AddActiveCertificateAsync(factory, authority);
        try
        {
            var before = await ConnectionAsync(ct);
            arca.Hold();
            var testing = factory.ExecuteScopeAsync(services => services.GetRequiredService<IArcaConnectionTestService>().TestAsync(ct));
            await WaitUntilAsync(() => arca.Calls("loginCms", "wsfe") + arca.Calls("loginCms", "ws_sr_constancia_inscripcion") > 0, ct);
            var chosen = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IArcaConnectionService>().ChooseServicesAsync([], before.Revision, ct));
            Assert.True(chosen.IsSuccess);
            arca.Release();

            Assert.Equal(ArcaConnectionErrors.RevisionConflict, (await testing).Error);
            Assert.Equal(before.LastTestedAtUtc, (await ConnectionAsync(ct)).LastTestedAtUtc);
        }
        finally
        {
            arca.Release();
            await ArcaPersistenceScenario.RemoveConnectionAsync(factory);
        }
    }

    [Fact]
    public async Task A_point_of_sale_checked_with_a_connection_that_was_replaced_is_a_conflict()
    {
        var ct = TestContext.Current.CancellationToken;
        var arca = Arca();
        arca.PointsOfSale = [new FakeArcaPointOfSale(5, "CAE - Factura", "N", "NULL")];
        await ArcaPersistenceScenario.AddActiveCertificateAsync(factory, authority);
        try
        {
            arca.Hold();
            var choosing = factory.ExecuteScopeAsync(services => services.GetRequiredService<IArcaInvoicingSetupService>()
                .ChoosePointOfSaleAsync(new ChooseArcaPointOfSaleRequest(5, 0), ct));
            await WaitUntilAsync(() => arca.Calls("loginCms", "wsfe") > 0, ct);
            await ArcaPersistenceScenario.RemoveConnectionAsync(factory);
            await ArcaPersistenceScenario.AddActiveCertificateAsync(factory, authority);
            arca.Release();

            Assert.Equal(ArcaConnectionErrors.RevisionConflict, (await choosing).Error);
            Assert.Null((await factory.ExecuteScopeAsync(services => services.GetRequiredService<IArcaInvoicingSetupService>().GetAsync(ct))).Value.PointOfSale);
        }
        finally
        {
            arca.Release();
            await ArcaPersistenceScenario.RemoveConnectionAsync(factory);
        }
    }

    public void Dispose() => authority.Dispose();

    private Task<ArquitecturaBase.Application.Modules.Arca.Models.Connection.ArcaConnectionRow> ConnectionAsync(CancellationToken ct) =>
        factory.ExecuteScopeAsync(async services => (await services.GetRequiredService<IArcaConnectionReader>().FindAsync(ct))!);

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (!condition()) await Task.Delay(20, timeout.Token);
    }

    private FakeArca Arca()
    {
        var arca = factory.Services.GetRequiredService<FakeArca>();
        arca.Reset();
        return arca;
    }
}
