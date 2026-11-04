using System.Net;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.Arca;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.Arca.Registry;

[Collection(ApiTestGroup.Name)]
public sealed class ArcaRegistryEndpointsTests(ApiFactory factory) : IDisposable
{
    private readonly FakeArcaCertificateAuthority authority = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_company_is_not_found_without_a_connection_and_empty_until_the_first_green_test()
    {
        await using var scenario = await ArcaHttpScenario.CreateAsync(factory, authority);
        var missing = await scenario.ReadAsync(await scenario.Client.GetAsync("/api/arca/registry/company", Ct), HttpStatusCode.NotFound);
        Assert.Equal("Arca.Connection.NotConnected", missing.GetProperty("code").GetString());

        await scenario.StartAsync([]);
        var empty = await scenario.ReadAsync(await scenario.Client.GetAsync("/api/arca/registry/company", Ct), HttpStatusCode.OK);

        Assert.Equal(JsonValueKind.Null, empty.GetProperty("legalName").ValueKind);
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("vatCondition").ValueKind);
    }

    public void Dispose() => authority.Dispose();
}
