using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.Arca;
using ArquitecturaBase.Application.Common.Exceptions;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.Arca.Invoicing;

[Collection(ApiTestGroup.Name)]
public sealed class ArcaInvoicingPersistenceTests(ApiFactory factory)
{
    [Fact]
    public async Task A_connection_has_one_invoicing_setting_that_goes_away_with_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionId = await ArcaPersistenceScenario.AddConnectionAsync(factory);
        try
        {
            await factory.InTransactionAsync(services =>
            {
                services.GetRequiredService<IArcaInvoicingRepository>().Add(ArcaInvoicingSettings.Create(connectionId));
                return Task.CompletedTask;
            });

            await Assert.ThrowsAsync<UniqueConstraintViolationException>(() => factory.InTransactionAsync(services =>
            {
                services.GetRequiredService<IArcaInvoicingRepository>().Add(ArcaInvoicingSettings.Create(connectionId));
                return Task.CompletedTask;
            }));

            await factory.InTransactionAsync(async services =>
            {
                var settings = await services.GetRequiredService<IArcaInvoicingRepository>().GetSettingsAsync(connectionId, ct);
                Assert.True(settings!.ChoosePointOfSale(7).IsSuccess);
            });

            var row = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IArcaInvoicingReader>().FindSettingsAsync(connectionId, ct));
            Assert.Equal(new ArcaInvoicingSettingsRow(7, Revision: 1), row);
        }
        finally
        {
            await ArcaPersistenceScenario.RemoveConnectionAsync(factory);
        }

        Assert.Null(await factory.ExecuteScopeAsync(services => services.GetRequiredService<IArcaInvoicingReader>().FindSettingsAsync(connectionId, ct)));
    }
}
