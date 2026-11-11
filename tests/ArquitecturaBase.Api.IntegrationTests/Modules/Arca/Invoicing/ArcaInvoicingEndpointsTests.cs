using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.Arca;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.Arca.Invoicing;

[Collection(ApiTestGroup.Name)]
public sealed class ArcaInvoicingEndpointsTests(ApiFactory factory) : IDisposable
{
    private readonly FakeArcaCertificateAuthority authority = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Points_of_sale_hide_the_dropped_ones_and_a_blocked_one_cannot_be_chosen()
    {
        await using var scenario = await ArcaHttpScenario.CreateAsync(factory, authority);
        await scenario.StartAsync([FakeArca.InvoicingService]);
        await scenario.UploadAsync();
        scenario.Arca.PointsOfSale =
        [
            new FakeArcaPointOfSale(1, "CAE - Factura", "N", "NULL"),
            new FakeArcaPointOfSale(2, "CAE - Factura", "S", "NULL"),
            new FakeArcaPointOfSale(3, "CAE - Factura", "N", "20250101"),
        ];

        var points = await scenario.ReadAsync(await scenario.Client.GetAsync("/api/arca/invoicing/points-of-sale", Ct), HttpStatusCode.OK);
        var blocked = await scenario.ReadAsync(await scenario.Client.PutAsJsonAsync("/api/arca/invoicing/point-of-sale", new { pointOfSale = 2 }, Ct),
            HttpStatusCode.BadRequest);

        Assert.Equal([(1, false), (2, true)], points.EnumerateArray().Select(point => (point.GetProperty("number").GetInt32(), point.GetProperty("blocked").GetBoolean())));
        Assert.Equal("Arca.Invoicing.PointOfSaleUnavailable", blocked.GetProperty("code").GetString());
        var settings = await scenario.ReadAsync(await scenario.Client.GetAsync("/api/arca/invoicing/settings", Ct), HttpStatusCode.OK);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, settings.GetProperty("pointOfSale").ValueKind);
    }

    [Fact]
    public async Task Points_of_sale_without_a_loaded_certificate_are_a_problem_with_its_code()
    {
        await using var scenario = await ArcaHttpScenario.CreateAsync(factory, authority);
        await scenario.StartAsync([FakeArca.InvoicingService]);

        var problem = await scenario.ReadAsync(await scenario.Client.GetAsync("/api/arca/invoicing/points-of-sale", Ct), HttpStatusCode.BadRequest);

        Assert.Equal("Arca.Connection.CertificateNotLoaded", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Voucher_data_is_saved_trimmed_and_read_back_with_the_point_of_sale()
    {
        await using var scenario = await ArcaHttpScenario.CreateAsync(factory, authority);
        await scenario.ActivateAsync(FakeArca.InvoicingService);

        var saved = await scenario.ReadAsync(await scenario.Client.PutAsJsonAsync("/api/arca/invoicing/voucher-data",
            new { grossIncomeNumber = "  901-123456-7 ", activityStartOn = "2019-03-01" }, Ct), HttpStatusCode.OK);
        var settings = await scenario.ReadAsync(await scenario.Client.GetAsync("/api/arca/invoicing/settings", Ct), HttpStatusCode.OK);

        foreach (var body in new[] { saved, settings })
        {
            Assert.Equal(5, body.GetProperty("pointOfSale").GetInt32());
            Assert.Equal("901-123456-7", body.GetProperty("grossIncomeNumber").GetString());
            Assert.Equal("2019-03-01", body.GetProperty("activityStartOn").GetString());
        }
    }

    [Fact]
    public async Task Voucher_data_without_the_gross_income_number_is_a_problem_and_keeps_what_was_saved()
    {
        await using var scenario = await ArcaHttpScenario.CreateAsync(factory, authority);
        await scenario.ActivateAsync(FakeArca.InvoicingService);
        var settings = await scenario.ReadAsync(await scenario.Client.GetAsync("/api/arca/invoicing/settings", Ct), HttpStatusCode.OK);

        var problem = await scenario.ReadAsync(await scenario.Client.PutAsJsonAsync("/api/arca/invoicing/voucher-data",
            new { grossIncomeNumber = " ", activityStartOn = "2019-03-01" }, Ct), HttpStatusCode.BadRequest);

        Assert.Equal("Arca.Invoicing.GrossIncomeInvalid", problem.GetProperty("code").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, settings.GetProperty("grossIncomeNumber").ValueKind);
        var after = await scenario.ReadAsync(await scenario.Client.GetAsync("/api/arca/invoicing/settings", Ct), HttpStatusCode.OK);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, after.GetProperty("grossIncomeNumber").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, after.GetProperty("activityStartOn").ValueKind);
    }

    [Fact]
    public async Task Voucher_data_without_a_connection_is_not_found()
    {
        await using var scenario = await ArcaHttpScenario.CreateAsync(factory, authority);

        var problem = await scenario.ReadAsync(await scenario.Client.PutAsJsonAsync("/api/arca/invoicing/voucher-data",
            new { grossIncomeNumber = "901-123456-7", activityStartOn = "2019-03-01" }, Ct), HttpStatusCode.NotFound);

        Assert.Equal("Arca.Connection.NotConnected", problem.GetProperty("code").GetString());
    }

    public void Dispose() => authority.Dispose();
}
