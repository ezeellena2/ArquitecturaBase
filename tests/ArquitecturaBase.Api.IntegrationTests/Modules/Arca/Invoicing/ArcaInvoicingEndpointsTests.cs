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
    public async Task Voucher_data_is_saved_trimmed_with_the_revision_it_read_and_read_back_with_the_point_of_sale()
    {
        await using var scenario = await ArcaHttpScenario.CreateAsync(factory, authority);
        await scenario.ActivateAsync(FakeArca.InvoicingService);
        var revision = (await scenario.ReadAsync(await scenario.Client.GetAsync("/api/arca/invoicing/settings", Ct), HttpStatusCode.OK))
            .GetProperty("revision").GetInt64();

        var saved = await scenario.ReadAsync(await scenario.Client.PutAsJsonAsync("/api/arca/invoicing/voucher-data",
            new { grossIncomeNumber = "  901-123456-7 ", activityStartOn = "2019-03-01", expectedRevision = revision }, Ct), HttpStatusCode.OK);
        var settings = await scenario.ReadAsync(await scenario.Client.GetAsync("/api/arca/invoicing/settings", Ct), HttpStatusCode.OK);

        Assert.Equal(1, revision);
        foreach (var body in new[] { saved, settings })
        {
            Assert.Equal(5, body.GetProperty("pointOfSale").GetInt32());
            Assert.Equal("901-123456-7", body.GetProperty("grossIncomeNumber").GetString());
            Assert.Equal("2019-03-01", body.GetProperty("activityStartOn").GetString());
            Assert.Equal(2, body.GetProperty("revision").GetInt64());
        }
    }

    [Fact]
    public async Task Voucher_data_without_the_gross_income_number_answers_by_field_and_keeps_what_was_saved()
    {
        await using var scenario = await ArcaHttpScenario.CreateAsync(factory, authority);
        await scenario.ActivateAsync(FakeArca.InvoicingService);

        var problem = await scenario.ReadAsync(await scenario.Client.PutAsJsonAsync("/api/arca/invoicing/voucher-data",
            new { grossIncomeNumber = " ", activityStartOn = "2019-03-01", expectedRevision = 1 }, Ct), HttpStatusCode.BadRequest);

        Assert.Equal("Validation.Failed", problem.GetProperty("code").GetString());
        Assert.Equal(["grossIncomeNumber"], problem.GetProperty("errors").EnumerateObject().Select(field => field.Name));
        var after = await scenario.ReadAsync(await scenario.Client.GetAsync("/api/arca/invoicing/settings", Ct), HttpStatusCode.OK);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, after.GetProperty("grossIncomeNumber").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, after.GetProperty("activityStartOn").ValueKind);
        Assert.Equal(1, after.GetProperty("revision").GetInt64());
    }

    [Fact]
    public async Task A_save_with_a_stale_revision_is_a_409_problem_and_keeps_what_was_saved()
    {
        await using var scenario = await ArcaHttpScenario.CreateAsync(factory, authority);
        await scenario.ActivateAsync(FakeArca.InvoicingService);
        await scenario.ReadAsync(await scenario.Client.PutAsJsonAsync("/api/arca/invoicing/voucher-data",
            new { grossIncomeNumber = "901-123456-7", activityStartOn = "2019-03-01", expectedRevision = 1 }, Ct), HttpStatusCode.OK);

        var voucher = await scenario.ReadAsync(await scenario.Client.PutAsJsonAsync("/api/arca/invoicing/voucher-data",
            new { grossIncomeNumber = "902-000000-1", activityStartOn = "2020-01-01", expectedRevision = 1 }, Ct), HttpStatusCode.Conflict);
        var point = await scenario.ReadAsync(await scenario.Client.PutAsJsonAsync("/api/arca/invoicing/point-of-sale",
            new { pointOfSale = 5, expectedRevision = 1 }, Ct), HttpStatusCode.Conflict);

        Assert.All(new[] { voucher, point }, problem => Assert.Equal("Arca.Invoicing.SettingsRevisionConflict", problem.GetProperty("code").GetString()));
        var after = await scenario.ReadAsync(await scenario.Client.GetAsync("/api/arca/invoicing/settings", Ct), HttpStatusCode.OK);
        Assert.Equal(("901-123456-7", 2L), (after.GetProperty("grossIncomeNumber").GetString(), after.GetProperty("revision").GetInt64()));
    }

    [Fact]
    public async Task A_point_of_sale_out_of_range_answers_by_field_without_asking_arca()
    {
        await using var scenario = await ArcaHttpScenario.CreateAsync(factory, authority);
        await scenario.ActivateAsync(FakeArca.InvoicingService);
        var asked = scenario.Arca.LastRequest("FEParamGetPtosVenta");

        var problem = await scenario.ReadAsync(await scenario.Client.PutAsJsonAsync("/api/arca/invoicing/point-of-sale",
            new { pointOfSale = 0, expectedRevision = 1 }, Ct), HttpStatusCode.BadRequest);

        Assert.Equal(["pointOfSale"], problem.GetProperty("errors").EnumerateObject().Select(field => field.Name));
        Assert.Same(asked, scenario.Arca.LastRequest("FEParamGetPtosVenta"));
    }

    [Fact]
    public async Task Voucher_data_without_a_connection_is_not_found()
    {
        await using var scenario = await ArcaHttpScenario.CreateAsync(factory, authority);

        var problem = await scenario.ReadAsync(await scenario.Client.PutAsJsonAsync("/api/arca/invoicing/voucher-data",
            new { grossIncomeNumber = "901-123456-7", activityStartOn = "2019-03-01", expectedRevision = 0 }, Ct), HttpStatusCode.NotFound);

        Assert.Equal("Arca.Connection.NotConnected", problem.GetProperty("code").GetString());
    }

    public void Dispose() => authority.Dispose();
}
