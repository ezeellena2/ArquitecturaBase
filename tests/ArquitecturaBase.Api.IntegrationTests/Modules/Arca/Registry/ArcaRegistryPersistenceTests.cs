using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.Arca;
using ArquitecturaBase.Application.Common.Exceptions;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Registry;
using ArquitecturaBase.Application.Modules.Arca.Models.Registry;
using ArquitecturaBase.Domain.Modules.Arca.Registry;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.Arca.Registry;

[Collection(ApiTestGroup.Name)]
public sealed class ArcaRegistryPersistenceTests(ApiFactory factory)
{
    [Fact]
    public async Task A_connection_has_one_company_that_goes_away_with_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionId = await ArcaPersistenceScenario.AddConnectionAsync(factory);
        try
        {
            await factory.InTransactionAsync(services =>
            {
                services.GetRequiredService<IArcaRegistryRepository>()
                    .Add(ArcaRegistryCompany.Create(connectionId, "Campo SA", ArcaVatCondition.RegisteredTaxpayer, "Ruta 9 km 400"));
                return Task.CompletedTask;
            });

            await Assert.ThrowsAsync<UniqueConstraintViolationException>(() => factory.InTransactionAsync(services =>
            {
                services.GetRequiredService<IArcaRegistryRepository>()
                    .Add(ArcaRegistryCompany.Create(connectionId, "Otra SA", ArcaVatCondition.Exempt, "Calle 1"));
                return Task.CompletedTask;
            }));

            await factory.InTransactionAsync(async services =>
            {
                var company = await services.GetRequiredService<IArcaRegistryRepository>().GetCompanyAsync(connectionId, ct);
                company!.Update("Campo y Hacienda SA", ArcaVatCondition.RegisteredTaxpayer, "Ruta 9 km 401");
            });

            var row = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IArcaRegistryReader>().FindCompanyAsync(connectionId, ct));
            Assert.Equal(new ArcaRegistryCompanyRow("Campo y Hacienda SA", ArcaVatCondition.RegisteredTaxpayer, "Ruta 9 km 401"), row);
        }
        finally
        {
            await ArcaPersistenceScenario.RemoveConnectionAsync(factory);
        }

        Assert.Null(await factory.ExecuteScopeAsync(services => services.GetRequiredService<IArcaRegistryReader>().FindCompanyAsync(connectionId, ct)));
    }
}
