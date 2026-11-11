using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.Arca;
using ArquitecturaBase.Application.Interfaces.Channels;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.Arca.Invoicing;

/// <summary>
/// ARCA como origen de los catálogos de formularios, con PostgreSQL y <c>FakeArca</c>, por el puerto neutral: sin conexión
/// devuelve por qué; con conexión, cada tabla vigente con su código y, en las alícuotas, Exento y No gravado.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class ArcaStructuredCatalogFeedTests(ApiFactory factory) : IDisposable
{
    private readonly FakeArcaCertificateAuthority authority = new();

    [Fact]
    public async Task Without_a_connection_the_feed_says_why_and_with_one_it_reads_each_table()
    {
        var ct = TestContext.Current.CancellationToken;
        factory.Services.GetRequiredService<FakeArca>().Reset();

        Assert.Equal("Arca.Connection.NotConnected", (await ReadAsync("arca.concepts")).FailureCode);
        Assert.Equal("Arca.Invoicing.CatalogUnknown", (await ReadAsync("arca.unknown", feedOf: "arca.concepts")).FailureCode);

        await ArcaVoucherScenario.ConnectAsync(factory, authority);
        try
        {
            var concepts = await ReadAsync("arca.concepts");
            Assert.Equal([("1", "1"), ("2", "2"), ("3", "3")], concepts.Items!.Select(item => (item.Code, item.Value!)));
            var rates = await ReadAsync("arca.vat-rates");
            Assert.Equal(["exempt", "not-taxed"], rates.Items!.TakeLast(2).Select(item => item.Code));
            var conditions = await ReadAsync("arca.vat-conditions");
            Assert.Equal(conditions.Items!.Count, conditions.Items.DistinctBy(item => item.Code).Count());
            Assert.Null((await ReadAsync("arca.currencies")).Items![0].Value);
        }
        finally
        {
            await ArcaVoucherScenario.RemoveAsync(factory);
        }

        // The feed that declares the key, or the one that declares feedOf when the key is one it does not keep.
        async Task<Application.Models.StructuredData.StructuredFeedReadResponse> ReadAsync(string key, string? feedOf = null)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetServices<IStructuredCatalogFeed>()
                .Single(feed => feed.Catalogs.Any(catalog => catalog.Key == (feedOf ?? key))).ReadAsync(key, ct);
        }
    }

    public void Dispose() => authority.Dispose();
}
