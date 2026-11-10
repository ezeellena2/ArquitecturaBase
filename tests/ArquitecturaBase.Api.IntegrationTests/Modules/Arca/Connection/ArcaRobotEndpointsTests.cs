using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.Arca;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Infrastructure.Modules.Arca.Connection.Robot;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.Arca.Connection;

/// <summary>
/// El robot de delegación por HTTP y de punta a punta (ADR 0026): la conexión en modo 3, el lado del cliente con la clave
/// en memoria, el de la plataforma con la reserva y la detección contra <see cref="FakeArca"/>, con Chromium recorriendo la
/// página simulada. Los workers están apagados: cada test los corre a mano con el procesador.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class ArcaRobotEndpointsTests(ApiFactory factory) : IDisposable
{
    private const string Route = "/api/arca/connection/robot-runs";
    private readonly FakeArcaCertificateAuthority authority = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private FakeArcaWeb Web => factory.Services.GetRequiredService<FakeArcaWeb>();
    private ArcaRobotProcessor Processor => factory.Services.GetRequiredService<ArcaRobotProcessor>();

    [Fact]
    public async Task Reading_needs_arca_read_and_starting_also_arca_manage()
    {
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Route + "/current", Ct)).StatusCode);

        using var reader = await ArcaHttpScenario.OperatorAsync(factory, [ArcaPermissions.Read]);
        using var read = await reader.GetAsync(Route + "/current", Ct);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var body = await read.ReadJsonAsync();
        Assert.True(body.GetProperty("enabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("company").ValueKind);

        using var start = await reader.PostAsJsonAsync(Route, Body(1, "clave"), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, start.StatusCode);
    }

    [Fact]
    public async Task The_consent_and_the_mode_are_checked_and_nothing_is_reserved()
    {
        await using var scenario = await ArcaHttpScenario.CreateAsync(factory, authority);
        var started = await scenario.StartAsync([FakeArca.InvoicingService]);
        var revision = started.GetProperty("revision").GetInt64();

        var noConsent = await scenario.ReadAsync(await scenario.Client.PostAsJsonAsync(Route, Body(revision, "clave") with { Consent = false }, Ct),
            HttpStatusCode.BadRequest);
        Assert.Equal("Arca.Connection.RobotConsentMissing", noConsent.GetProperty("code").GetString());

        var wrongMode = await scenario.ReadAsync(await scenario.Client.PostAsJsonAsync(Route, Body(revision, "clave"), Ct), HttpStatusCode.BadRequest);
        Assert.Equal("Arca.Connection.RobotModeMismatch", wrongMode.GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.Null, (await CurrentAsync(scenario)).GetProperty("company").ValueKind);
    }

    [Fact]
    public async Task The_robot_delegates_creates_the_certificate_accepts_assigns_and_confirms_without_keeping_the_key()
    {
        await using var scenario = await StartAutomatedAsync();
        var key = "clave-" + Guid.NewGuid().ToString("N");
        Web.Keys[FakeArcaWeb.ClientLogin] = key;
        scenario.Arca.Relations = [FakeArcaWeb.Company];
        var revision = (await scenario.GetConnectionAsync()).GetProperty("revision").GetInt64();

        var accepted = await scenario.ReadAsync(await scenario.Client.PostAsJsonAsync(Route, Body(revision, key), Ct), HttpStatusCode.Accepted);
        Assert.Equal("Running", accepted.GetProperty("company").GetProperty("state").GetString());
        Assert.Equal("Waiting", accepted.GetProperty("platform").GetProperty("state").GetString());

        var (job, credentials) = await TakeJobAsync();
        await Processor.RunCompanyAsync(job, credentials, Ct);
        await Processor.ProcessDuePlatformAsync(Ct);

        var current = await CurrentAsync(scenario);
        var company = current.GetProperty("company");
        Assert.Equal("Succeeded", company.GetProperty("state").GetString());
        Assert.Equal(["company.login", "company.delegate.wsfe", "company.point-of-sale"], Steps(company));
        Assert.Equal("2", company.GetProperty("outputs").GetProperty("company.point-of-sale").GetString());
        var platform = current.GetProperty("platform");
        Assert.Equal("Succeeded", platform.GetProperty("state").GetString());
        Assert.Equal(["platform.login", "platform.certificate", "platform.authorize.ws_sr_constancia_inscripcion", "platform.accept",
            "platform.assign.wsfe", "verify"], Steps(platform));

        var connection = await scenario.GetConnectionAsync();
        Assert.True(connection.GetProperty("lastTestPassed").GetBoolean());
        Assert.Contains(connection.GetProperty("certificates").EnumerateArray(),
            certificate => certificate.GetProperty("role").GetString() == "Active" && certificate.GetProperty("isLoaded").GetBoolean());
        Assert.Contains(new FakeArcaWebRelation(FakeArcaWeb.Company, FakeArca.InvoicingService, "plataforma-demo", Accepted: true), Web.Relations);

        // La clave del cliente no quedó en ninguna respuesta, log, excepción ni fila del módulo.
        Assert.DoesNotContain(scenario.Bodies, body => body.Contains(key, StringComparison.Ordinal));
        Assert.False(factory.Services.GetRequiredService<FakeArcaLogCapture>().Contains(key));
        Assert.DoesNotContain(await RowsAsync(), row => row.Contains(key, StringComparison.Ordinal));
        Assert.DoesNotContain(await RowsAsync(), row => row.Contains(FakeArcaWeb.ClientLogin, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_changed_screen_on_the_platform_side_stops_it_and_mails_the_owner()
    {
        await using var scenario = await StartAutomatedAsync();
        Web.Broken = "certs";
        var revision = (await scenario.GetConnectionAsync()).GetProperty("revision").GetInt64();
        var before = factory.EmailSender.CountFor(ArcaTestModule.RobotAlertEmail);
        await scenario.ReadAsync(await scenario.Client.PostAsJsonAsync(Route, Body(revision, FakeArcaWeb.ClientKey), Ct), HttpStatusCode.Accepted);
        var (job, credentials) = await TakeJobAsync();
        await Processor.RunCompanyAsync(job, credentials, Ct);

        await Processor.ProcessDuePlatformAsync(Ct);

        var platform = (await CurrentAsync(scenario)).GetProperty("platform");
        Assert.Equal("Failed", platform.GetProperty("state").GetString());
        Assert.Equal("ScreenChanged", platform.GetProperty("failure").GetString());
        Assert.Equal("platform.certificate", platform.GetProperty("failedStep").GetString());
        var alert = await factory.EmailSender.WaitForAsync(ArcaTestModule.RobotAlertEmail, before + 1);
        Assert.Contains("platform.certificate", alert.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeArcaWeb.Company, alert.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_company_side_whose_host_died_reads_interrupted_and_starts_again()
    {
        await using var scenario = await StartAutomatedAsync();
        var revision = (await scenario.GetConnectionAsync()).GetProperty("revision").GetInt64();
        await scenario.ReadAsync(await scenario.Client.PostAsJsonAsync(Route, Body(revision, FakeArcaWeb.ClientKey), Ct), HttpStatusCode.Accepted);

        var busy = await scenario.ReadAsync(await scenario.Client.PostAsJsonAsync(Route, Body(revision, FakeArcaWeb.ClientKey), Ct), HttpStatusCode.Conflict);
        Assert.Equal("Arca.Connection.RobotRunInProgress", busy.GetProperty("code").GetString());

        factory.Clock.Advance(TimeSpan.FromMinutes(4));
        Assert.Equal("Interrupted", (await CurrentAsync(scenario)).GetProperty("company").GetProperty("state").GetString());
        await scenario.ReadAsync(await scenario.Client.PostAsJsonAsync(Route, Body(revision, FakeArcaWeb.ClientKey), Ct), HttpStatusCode.Accepted);
        await DrainAsync();
    }

    [Fact]
    public async Task Two_hosts_never_take_the_same_platform_side()
    {
        await using var scenario = await StartAutomatedAsync();
        var revision = (await scenario.GetConnectionAsync()).GetProperty("revision").GetInt64();
        await scenario.ReadAsync(await scenario.Client.PostAsJsonAsync(Route, Body(revision, FakeArcaWeb.ClientKey), Ct), HttpStatusCode.Accepted);
        await DrainAsync();
        Guid runId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            runId = Assert.Single(await scope.ServiceProvider.GetRequiredService<IArcaRobotRunReader>()
                .ListDuePlatformRunsAsync(factory.Clock.GetUtcNow().UtcDateTime, Ct));
        }

        var reservations = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            await using var scope = factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IArcaRobotRunStore>().ReservePlatformAsync(runId, Guid.NewGuid(), Ct);
        }));

        Assert.Single(reservations, job => job is not null);
    }

    public void Dispose() => authority.Dispose();

    /// <summary>La conexión en modo 3 con su pedido de la plataforma, la página simulada limpia y la cola del host vacía.</summary>
    private async Task<ArcaHttpScenario> StartAutomatedAsync()
    {
        Web.Reset();
        Web.IssueCertificate = request => authority.Issue(request, ArcaEnvironment.Testing, factory.Clock.GetUtcNow().UtcDateTime.AddYears(1));
        await DrainAsync();
        var scenario = await ArcaHttpScenario.CreateAsync(factory, authority);
        await scenario.StartAsync([FakeArca.InvoicingService], alias: "plataforma-demo", mode: "PlatformDelegationAutomated");
        return scenario;
    }

    private async Task<(ArcaCompanyRobotWorkResponse Job, ArcaFiscalCredentials Credentials)> TakeJobAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await foreach (var item in factory.Services.GetRequiredService<ArcaCompanyRobotQueue>().ReadAllAsync(timeout.Token)) return item;
        throw new InvalidOperationException("The robot queue is empty.");
    }

    private async Task DrainAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(200));
        try
        {
            await foreach (var _ in factory.Services.GetRequiredService<ArcaCompanyRobotQueue>().ReadAllAsync(timeout.Token)) { }
        }
        catch (OperationCanceledException) when (!Ct.IsCancellationRequested)
        {
        }
    }

    /// <summary>Las filas del robot, la conexión y los certificados como texto, para buscar la clave.</summary>
    private async Task<List<string>> RowsAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Database.SqlQueryRaw<string>(
            "SELECT row_to_json(r)::text AS \"Value\" FROM \"ArcaRobotRuns\" r UNION ALL SELECT row_to_json(c)::text FROM \"ArcaConnections\" c "
            + "UNION ALL SELECT row_to_json(x)::text FROM \"ArcaCertificates\" x").ToListAsync(Ct);
    }

    private static async Task<JsonElement> CurrentAsync(ArcaHttpScenario scenario) =>
        await scenario.ReadAsync(await scenario.Client.GetAsync(Route + "/current", Ct), HttpStatusCode.OK);

    private static string?[] Steps(JsonElement side) => [.. side.GetProperty("completedSteps").EnumerateArray().Select(step => step.GetString())];

    private static RobotBody Body(long revision, string key) => new(true, "20-28456789-8", key, revision);

    private sealed record RobotBody(bool Consent, string LoginCuit, string FiscalKey, long ExpectedRevision);
}
