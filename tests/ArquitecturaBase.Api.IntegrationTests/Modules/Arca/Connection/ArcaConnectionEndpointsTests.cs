using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.Arca.Connection;

/// <summary>Las rutas de la conexión con FakeArca y la CA de prueba: el alta entera, la renovación sin corte y los errores.</summary>
[Collection(ApiTestGroup.Name)]
public sealed class ArcaConnectionEndpointsTests(ApiFactory factory) : IDisposable
{
    private readonly FakeArcaCertificateAuthority authority = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("GET", "/api/arca/connection", false)]
    [InlineData("POST", "/api/arca/connection/certificate-requests", true)]
    [InlineData("GET", "/api/arca/connection/certificate-requests/current", true)]
    [InlineData("DELETE", "/api/arca/connection/certificate-requests/current?expectedRevision=1", true)]
    [InlineData("PUT", "/api/arca/connection/services", true)]
    [InlineData("PUT", "/api/arca/connection/certificate", true)]
    [InlineData("POST", "/api/arca/connection/test", true)]
    [InlineData("DELETE", "/api/arca/connection?expectedRevision=1", true)]
    [InlineData("GET", "/api/arca/registry/company", false)]
    [InlineData("GET", "/api/arca/invoicing/settings", false)]
    [InlineData("GET", "/api/arca/invoicing/points-of-sale", true)]
    [InlineData("PUT", "/api/arca/invoicing/point-of-sale", true)]
    public async Task Every_route_requires_a_session_read_and_for_writes_manage(string method, string path, bool manages)
    {
        using var anonymous = factory.CreateClient();
        using var reader = await ArcaHttpScenario.OperatorAsync(factory, [ArcaPermissions.Read]);
        using var manager = await ArcaHttpScenario.OperatorAsync(factory, [ArcaPermissions.Manage]);

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(anonymous, method, path));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(manager, method, path));
        if (manages) Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(reader, method, path));
    }

    [Fact]
    public async Task Without_a_connection_the_screen_gets_no_status_the_installed_services_and_revision_zero()
    {
        await using var scenario = await ArcaHttpScenario.CreateAsync(factory, authority);

        var connection = await scenario.GetConnectionAsync();

        Assert.Equal(System.Text.Json.JsonValueKind.Null, connection.GetProperty("status").ValueKind);
        Assert.Equal(0, connection.GetProperty("revision").GetInt64());
        Assert.Equal(["ws_sr_constancia_inscripcion", "wsfe"],
            connection.GetProperty("installedServices").EnumerateArray().Select(service => service.GetProperty("code").GetString()));
    }

    [Fact]
    public async Task The_complete_alta_tests_on_upload_and_activates_with_the_point_of_sale_without_exposing_secrets()
    {
        await using var scenario = await ArcaHttpScenario.CreateAsync(factory, authority);

        var started = await scenario.StartAsync([FakeArca.InvoicingService]);
        Assert.Equal("AwaitingCertificate", started.GetProperty("status").GetString());
        using (var request = await scenario.Client.GetAsync("/api/arca/connection/certificate-requests/current", Ct))
        {
            Assert.Equal("application/pkcs10", request.Content.Headers.ContentType?.MediaType);
            Assert.Equal("arca-plataforma.csr", request.Content.Headers.ContentDisposition?.FileNameStar ?? request.Content.Headers.ContentDisposition?.FileName);
            Assert.StartsWith("-----BEGIN CERTIFICATE REQUEST-----", await request.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        }

        var tested = await scenario.UploadAsync();
        Assert.All(tested.GetProperty("checks").EnumerateArray(), check => Assert.Equal("Ready", check.GetProperty("status").GetString()));
        Assert.Equal("Unverified", tested.GetProperty("connection").GetProperty("status").GetString());
        var company = await scenario.ReadAsync(await scenario.Client.GetAsync("/api/arca/registry/company", Ct), HttpStatusCode.OK);
        Assert.Equal(("Campo SA", "RegisteredTaxpayer"), (company.GetProperty("legalName").GetString(), company.GetProperty("vatCondition").GetString()));

        scenario.Arca.PointsOfSale = [new FakeArcaPointOfSale(5, "CAE - Factura", "N", "NULL")];
        var points = await scenario.ReadAsync(await scenario.Client.GetAsync("/api/arca/invoicing/points-of-sale", Ct), HttpStatusCode.OK);
        Assert.Equal(5, Assert.Single(points.EnumerateArray()).GetProperty("number").GetInt32());
        await scenario.ReadAsync(await scenario.Client.PutAsJsonAsync("/api/arca/invoicing/point-of-sale", new { pointOfSale = 5 }, Ct), HttpStatusCode.OK);
        Assert.Equal("Active", (await scenario.GetConnectionAsync()).GetProperty("status").GetString());

        Assert.All(scenario.Bodies, body =>
        {
            Assert.DoesNotContain("PRIVATE KEY", body, StringComparison.Ordinal);
            Assert.DoesNotContain("token-", body, StringComparison.Ordinal);
            Assert.DoesNotContain("sign-", body, StringComparison.Ordinal);
            Assert.DoesNotContain("protected", body, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task Without_invoicing_the_connection_is_active_without_a_point_of_sale()
    {
        await using var scenario = await ArcaHttpScenario.CreateAsync(factory, authority);

        await scenario.StartAsync([]);
        var tested = await scenario.UploadAsync();

        Assert.Equal("Active", tested.GetProperty("connection").GetProperty("status").GetString());
        var settings = await scenario.ReadAsync(await scenario.Client.GetAsync("/api/arca/invoicing/settings", Ct), HttpStatusCode.OK);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, settings.GetProperty("pointOfSale").ValueKind);
    }

    [Fact]
    public async Task A_stale_revision_is_a_409_problem_with_its_code()
    {
        await using var scenario = await ArcaHttpScenario.CreateAsync(factory, authority);
        var started = await scenario.StartAsync([]);
        var stale = started.GetProperty("revision").GetInt64() - 1;

        var problem = await scenario.ReadAsync(await scenario.Client.PutAsJsonAsync("/api/arca/connection/services",
            new { services = Array.Empty<string>(), expectedRevision = stale }, Ct), HttpStatusCode.Conflict);

        Assert.Equal("Arca.Connection.RevisionConflict", problem.GetProperty("code").GetString());
        Assert.True(problem.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task A_service_that_is_not_authorized_answers_200_with_its_check_and_the_connection_needing_attention()
    {
        await using var scenario = await ArcaHttpScenario.CreateAsync(factory, authority);
        var active = await scenario.ActivateAsync();
        await scenario.ReadAsync(await scenario.Client.PutAsJsonAsync("/api/arca/connection/services",
            new { services = new[] { FakeArca.InvoicingService }, expectedRevision = active.GetProperty("revision").GetInt64() }, Ct), HttpStatusCode.OK);
        scenario.Arca.Fail(FakeArca.InvoicingService, FakeArcaFailure.NotAuthorized);

        var tested = await scenario.ReadAsync(await scenario.Client.PostAsync("/api/arca/connection/test", null, Ct), HttpStatusCode.OK);

        var invoicing = tested.GetProperty("checks").EnumerateArray().Single(check => check.GetProperty("service").GetString() == FakeArca.InvoicingService);
        Assert.Equal("NotAuthorized", invoicing.GetProperty("status").GetString());
        Assert.Equal("NeedsAttention", tested.GetProperty("connection").GetProperty("status").GetString());
    }

    [Fact]
    public async Task The_renewal_does_not_cut_the_connection_and_replaces_the_certificate_when_its_test_is_green()
    {
        await using var scenario = await ArcaHttpScenario.CreateAsync(factory, authority);
        var active = await scenario.ActivateAsync();

        var requested = await scenario.StartAsync([], "renovacion", active.GetProperty("revision").GetInt64());
        Assert.Equal([("Active", "plataforma"), ("Pending", "renovacion")], Certificates(requested));
        var meanwhile = await scenario.ReadAsync(await scenario.Client.PostAsync("/api/arca/connection/test", null, Ct), HttpStatusCode.OK);
        Assert.Equal("Active", meanwhile.GetProperty("connection").GetProperty("status").GetString());
        Assert.Equal([("Active", "plataforma"), ("Pending", "renovacion")], Certificates(meanwhile.GetProperty("connection")));

        var renewed = await scenario.UploadAsync();

        Assert.Equal([("Active", "renovacion")], Certificates(renewed.GetProperty("connection")));
        Assert.Equal("Active", renewed.GetProperty("connection").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Disconnecting_leaves_the_screen_without_a_connection()
    {
        await using var scenario = await ArcaHttpScenario.CreateAsync(factory, authority);
        var active = await scenario.ActivateAsync();

        using var deleted = await scenario.Client.DeleteAsync($"/api/arca/connection?expectedRevision={active.GetProperty("revision").GetInt64()}", Ct);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(0, (await scenario.GetConnectionAsync()).GetProperty("revision").GetInt64());
    }

    [Fact]
    public async Task The_delegated_alta_requests_for_the_platform_detects_the_delegation_and_consults_the_registry_as_the_platform()
    {
        await using var scenario = await ArcaHttpScenario.CreateAsync(factory, authority);
        scenario.Arca.Relations = [ArcaPersistenceScenario.PlatformCuit];

        var empty = await scenario.GetConnectionAsync();
        Assert.Equal((ArcaPersistenceScenario.PlatformCuit, "Plataforma Ejemplo S.A."),
            (empty.GetProperty("platform").GetProperty("cuit").GetString(), empty.GetProperty("platform").GetProperty("name").GetString()));
        Assert.Equal([("ws_sr_constancia_inscripcion", false), ("wsfe", true)], empty.GetProperty("installedServices").EnumerateArray()
            .Select(service => (service.GetProperty("code").GetString(), service.GetProperty("delegated").GetBoolean())));

        var started = await scenario.StartAsync([FakeArca.InvoicingService], mode: "PlatformDelegation");
        Assert.Equal(("PlatformDelegation", ArcaPersistenceScenario.Cuit, ArcaPersistenceScenario.PlatformCuit),
            (started.GetProperty("mode").GetString(), started.GetProperty("cuit").GetString(), started.GetProperty("platformCuit").GetString()));

        // Todavía no llegó la delegación: el padrón responde con la CUIT de la plataforma y Facturación queda sin delegar.
        var waiting = await scenario.UploadAsync();
        Assert.Equal([("ws_sr_constancia_inscripcion", "Ready"), ("wsfe", "NotDelegated")], Checks(waiting));
        Assert.Equal("Unverified", waiting.GetProperty("connection").GetProperty("status").GetString());
        var consulted = scenario.Arca.LastRequest("getPersona_v2")!;
        Assert.Equal((ArcaPersistenceScenario.PlatformCuit, ArcaPersistenceScenario.Cuit),
            (consulted.Element("cuitRepresentada")?.Value, consulted.Element("idPersona")?.Value));

        // La empresa delegó y la plataforma aceptó y asignó: verificar renueva el ticket y Facturación responde.
        scenario.Arca.Relations = [ArcaPersistenceScenario.PlatformCuit, ArcaPersistenceScenario.Cuit];
        var delegated = await scenario.ReadAsync(await scenario.Client.PostAsync("/api/arca/connection/test", null, Ct), HttpStatusCode.OK);
        Assert.Equal([("ws_sr_constancia_inscripcion", "Ready"), ("wsfe", "Ready")], Checks(delegated));

        scenario.Arca.PointsOfSale = [new FakeArcaPointOfSale(5, "CAE - Factura", "N", "NULL")];
        await scenario.ReadAsync(await scenario.Client.PutAsJsonAsync("/api/arca/invoicing/point-of-sale", new { pointOfSale = 5 }, Ct), HttpStatusCode.OK);
        Assert.Equal(ArcaPersistenceScenario.Cuit, scenario.Arca.LastRequest("FEParamGetPtosVenta")!.Descendants().Single(element => element.Name.LocalName == "Cuit").Value);
        Assert.Equal("Active", (await scenario.GetConnectionAsync()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task The_delegated_alta_by_robot_is_not_available_yet()
    {
        await using var scenario = await ArcaHttpScenario.CreateAsync(factory, authority);

        var body = new { cuit = "20-00000006-0", environment = "Testing", alias = "plataforma", services = Array.Empty<string>(), expectedRevision = 0,
            mode = "PlatformDelegationAutomated" };
        var problem = await scenario.ReadAsync(await scenario.Client.PostAsJsonAsync("/api/arca/connection/certificate-requests", body, Ct),
            HttpStatusCode.BadRequest);

        Assert.Equal("Arca.Connection.ModeUnavailable", problem.GetProperty("code").GetString());
        Assert.Equal(0, (await scenario.GetConnectionAsync()).GetProperty("revision").GetInt64());
    }

    public void Dispose() => authority.Dispose();

    private static (string?, string?)[] Checks(System.Text.Json.JsonElement tested) =>
        [.. tested.GetProperty("checks").EnumerateArray().Select(check => (check.GetProperty("service").GetString(), check.GetProperty("status").GetString()))];

    private static (string?, string?)[] Certificates(System.Text.Json.JsonElement connection) =>
        [.. connection.GetProperty("certificates").EnumerateArray()
            .Select(certificate => (certificate.GetProperty("role").GetString(), certificate.GetProperty("alias").GetString()))];

    private static async Task<HttpStatusCode> StatusAsync(HttpClient client, string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = method is "GET" or "DELETE" ? null : JsonContent.Create(new { }) };
        using var response = await client.SendAsync(request, Ct);
        return response.StatusCode;
    }
}
