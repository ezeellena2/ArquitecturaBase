using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.Arca;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Infrastructure.Modules.Arca.Connection.Robot;
using ArquitecturaBase.Infrastructure.Modules.Arca.Invoicing.Robot;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.Arca.Connection;

/// <summary>
/// El robot de Playwright contra las páginas simuladas de ARCA, con Chromium sin ventana (ADR 0026). Comparte la página
/// simulada con los tests HTTP del robot, por eso va en el mismo grupo.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class ArcaDelegationRobotTests : IAsyncLifetime
{
    private const string Wsfe = "wsfe";
    private const string Registry = "ws_sr_constancia_inscripcion";
    private const string Alias = "plataforma-demo";
    private readonly FakeArcaWeb web = FakeArcaWeb.Shared;
    private PlaywrightArcaDelegationRobot robot = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        web.Reset();
        robot = new PlaywrightArcaDelegationRobot([new ArcaPointOfSaleRobotStep()], Options.Create(new ArcaRobotBrowserOptions
        {
            Enabled = true,
            LoginUrl = web.LoginUrl,
            StepTimeout = TimeSpan.FromSeconds(4),
            RunTimeout = TimeSpan.FromMinutes(1),
            Platform = new ArcaRobotPlatformCredentials { LoginCuit = "30-71999888-3", FiscalKey = FakeArcaWeb.PlatformKey },
        }), TimeProvider.System, NullLogger<PlaywrightArcaDelegationRobot>.Instance);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => robot.DisposeAsync();

    [Fact]
    public async Task The_company_side_delegates_each_service_and_adds_a_web_service_point_of_sale()
    {
        var progress = new RecordingProgress();
        var credentials = Client();

        var outcome = await robot.RunCompanyAsync(CompanyJob(), credentials, progress, Ct);

        Assert.Equal(ArcaRobotOutcome.Success, outcome);
        // H21: the login took the key once; nothing keeps it after it.
        Assert.True(credentials.Key.IsEmpty);
        Assert.Equal(["company.login", "company.delegate.wsfe", "company.point-of-sale"], progress.Completed);
        Assert.Equal("2", progress.Outputs["company.point-of-sale"]);
        var relation = Assert.Single(web.Relations);
        Assert.Equal(new FakeArcaWebRelation(FakeArcaWeb.Company, Wsfe, FakeArcaWeb.Platform, Accepted: false), relation);
        Assert.Contains(web.Points, point => point is { Owner: FakeArcaWeb.Company, Number: 2, Dropped: false } && point.System.EndsWith("Web Services", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Running_the_company_side_again_duplicates_nothing()
    {
        var first = new RecordingProgress();
        await robot.RunCompanyAsync(CompanyJob(), Client(), first, Ct);
        var again = new RecordingProgress();

        Assert.Equal(ArcaRobotOutcome.Success, await robot.RunCompanyAsync(CompanyJob(first.Outputs), Client(), again, Ct));

        Assert.Single(web.Relations);
        Assert.Equal(2, web.Points.Count(point => point.Owner == FakeArcaWeb.Company));
        Assert.Equal("2", again.Outputs["company.point-of-sale"]);
    }

    // O10: the robot adds a point of sale of its own: a web service one of another system of the company is not taken.
    [Fact]
    public async Task A_web_service_point_of_sale_of_another_system_is_not_taken()
    {
        web.Points.Add(new FakeArcaWebPoint(FakeArcaWeb.Company, 3, "Otro sistema - Web Services", false));
        var progress = new RecordingProgress();

        Assert.Equal(ArcaRobotOutcome.Success, await robot.RunCompanyAsync(CompanyJob(), Client(), progress, Ct));

        Assert.Equal("4", progress.Outputs["company.point-of-sale"]);
        Assert.Equal(3, web.Points.Count(point => point.Owner == FakeArcaWeb.Company));
    }

    // O10: the number is recorded before confirming it in ARCA: a pass cut after the confirmation leaves it, and the next pass
    // takes it from the table instead of adding another.
    [Fact]
    public async Task A_point_of_sale_confirmed_before_a_cut_is_taken_by_the_next_pass()
    {
        var cut = new RecordingProgress { LoseAtComplete = "company.point-of-sale" };

        Assert.Equal(ArcaRobotOutcome.Stopped(ArcaRobotFailure.Interrupted, "company.point-of-sale"),
            await robot.RunCompanyAsync(CompanyJob(), Client(), cut, Ct));
        Assert.Equal("2", cut.Outputs["company.point-of-sale"]);

        var again = new RecordingProgress();
        Assert.Equal(ArcaRobotOutcome.Success, await robot.RunCompanyAsync(CompanyJob(cut.Outputs), Client(), again, Ct));
        Assert.Equal("2", again.Outputs["company.point-of-sale"]);
        Assert.Equal(2, web.Points.Count(point => point.Owner == FakeArcaWeb.Company));
    }

    [Fact]
    public async Task A_wrong_key_stops_at_the_login_without_trying_again()
    {
        var outcome = await robot.RunCompanyAsync(CompanyJob(), new ArcaFiscalCredentials(FakeArcaWeb.ClientLogin, new ArcaFiscalKey("otra")),
            new RecordingProgress(), Ct);

        Assert.Equal(ArcaRobotOutcome.Stopped(ArcaRobotFailure.LoginRejected, ArcaRobotSteps.CompanyLogin), outcome);
        Assert.Equal(1, web.Logins(FakeArcaWeb.ClientLogin));
        Assert.Empty(web.Relations);
    }

    [Fact]
    public async Task A_captcha_or_an_extra_validation_stops_the_robot_before_doing_anything()
    {
        web.Captcha = true;
        Assert.Equal(ArcaRobotOutcome.Stopped(ArcaRobotFailure.Captcha, ArcaRobotSteps.CompanyLogin),
            await robot.RunCompanyAsync(CompanyJob(), Client(), new RecordingProgress(), Ct));

        web.Captcha = false;
        web.ExtraValidation = true;
        Assert.Equal(ArcaRobotOutcome.Stopped(ArcaRobotFailure.ExtraValidation, ArcaRobotSteps.CompanyLogin),
            await robot.RunCompanyAsync(CompanyJob(), Client(), new RecordingProgress(), Ct));
        Assert.Empty(web.Relations);
    }

    [Fact]
    public async Task A_changed_screen_stops_at_its_step_and_keeps_what_was_done()
    {
        web.Broken = "pos";
        var progress = new RecordingProgress();

        var outcome = await robot.RunCompanyAsync(CompanyJob(), Client(), progress, Ct);

        Assert.Equal(ArcaRobotOutcome.Stopped(ArcaRobotFailure.ScreenChanged, ArcaPointOfSaleRobotStep.StepKey), outcome);
        Assert.Equal(["company.login", "company.delegate.wsfe"], progress.Completed);
        Assert.Single(web.Relations);
    }

    // H18: a page that does not load in time or answers a 5xx is ARCA not answering, which the platform side retries; a changed
    // screen is a loaded page that lacks what is expected.
    [Theory]
    [InlineData("pos")]
    [InlineData("pos-5xx")]
    public async Task A_page_that_does_not_load_or_answers_a_server_error_is_arca_not_answering(string down)
    {
        web.Down = down;

        var outcome = await robot.RunCompanyAsync(CompanyJob(), Client(), new RecordingProgress(), Ct);

        Assert.Equal(ArcaRobotOutcome.Stopped(ArcaRobotFailure.Unavailable, ArcaPointOfSaleRobotStep.StepKey), outcome);
    }

    // H20: a step longer than the step timeout renews its lease with a heartbeat while it runs.
    [Fact]
    public async Task A_long_step_renews_its_lease_while_it_runs()
    {
        web.Slow = true;
        var progress = new RecordingProgress();

        var outcome = await robot.RunCompanyAsync(CompanyJob(), Client(), progress, Ct);

        Assert.Equal(ArcaRobotOutcome.Success, outcome);
        Assert.True(progress.Renewals > 0);
    }

    [Fact]
    public async Task A_lost_lease_stops_the_robot_without_another_step()
    {
        var progress = new RecordingProgress { LoseAfter = "company.login" };

        var outcome = await robot.RunCompanyAsync(CompanyJob(), Client(), progress, Ct);

        Assert.Equal(ArcaRobotOutcome.Stopped(ArcaRobotFailure.Interrupted, "company.delegate.wsfe"), outcome);
        Assert.Empty(web.Relations);
    }

    [Fact]
    public async Task The_platform_side_creates_the_certificate_authorizes_accepts_and_assigns()
    {
        web.IssueCertificate = request => "-----BEGIN CERTIFICATE-----\nISSUED:" + request.Length + "\n-----END CERTIFICATE-----\n";
        await robot.RunCompanyAsync(CompanyJob(), Client(), new RecordingProgress(), Ct);
        var progress = new RecordingProgress();

        var outcome = await robot.RunPlatformAsync(PlatformJob(request: "-----BEGIN CERTIFICATE REQUEST-----\nCSR\n-----END CERTIFICATE REQUEST-----\n", companyDone: true),
            progress, Ct);

        Assert.Equal(ArcaRobotOutcome.Success, outcome);
        Assert.Equal(["platform.login", "platform.certificate", "platform.authorize.ws_sr_constancia_inscripcion", "platform.accept", "platform.assign.wsfe"],
            progress.Completed);
        Assert.StartsWith("-----BEGIN CERTIFICATE-----", Assert.Single(progress.Certificates), StringComparison.Ordinal);
        Assert.Equal(Alias, Assert.Single(web.Aliases).Alias);
        Assert.Contains(new FakeArcaWebRelation(FakeArcaWeb.Platform, Registry, Alias, Accepted: true), web.Relations);
        Assert.Contains(new FakeArcaWebRelation(FakeArcaWeb.Company, Wsfe, FakeArcaWeb.Platform, Accepted: true), web.Relations);
        Assert.Contains(new FakeArcaWebRelation(FakeArcaWeb.Company, Wsfe, Alias, Accepted: true), web.Relations);
    }

    [Fact]
    public async Task Without_the_company_the_platform_side_does_only_its_own_steps()
    {
        web.IssueCertificate = _ => "-----BEGIN CERTIFICATE-----\nX\n-----END CERTIFICATE-----\n";
        var progress = new RecordingProgress();

        Assert.Equal(ArcaRobotOutcome.Success, await robot.RunPlatformAsync(PlatformJob(request: "CSR", companyDone: false), progress, Ct));

        Assert.Equal(["platform.login", "platform.certificate", "platform.authorize.ws_sr_constancia_inscripcion"], progress.Completed);
    }

    [Fact]
    public async Task An_existing_alias_gets_a_new_certificate_instead_of_another_alias()
    {
        var issued = 0;
        web.IssueCertificate = _ => $"-----BEGIN CERTIFICATE-----\nN{++issued}\n-----END CERTIFICATE-----\n";
        await robot.RunPlatformAsync(PlatformJob(request: "CSR", companyDone: false), new RecordingProgress(), Ct);
        var progress = new RecordingProgress();

        await robot.RunPlatformAsync(PlatformJob(request: "CSR", companyDone: false), progress, Ct);

        Assert.Equal(2, Assert.Single(web.Aliases).Certificates.Count);
        Assert.Contains("N2", Assert.Single(progress.Certificates), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_platform_side_with_nothing_pending_does_not_open_arca()
    {
        var progress = new RecordingProgress();
        var job = PlatformJob(request: null, companyDone: true) with
        {
            CompletedSteps = ["platform.authorize.ws_sr_constancia_inscripcion", "platform.accept", "platform.assign.wsfe"],
        };

        Assert.Equal(ArcaRobotOutcome.Success, await robot.RunPlatformAsync(job, progress, Ct));

        Assert.Equal(["platform.certificate"], progress.Completed);
        Assert.Equal(0, web.Logins(FakeArcaWeb.Platform));
    }

    [Fact]
    public async Task A_certificate_that_does_not_pass_the_upload_stops_the_platform_side()
    {
        web.IssueCertificate = _ => "-----BEGIN CERTIFICATE-----\nX\n-----END CERTIFICATE-----\n";
        var progress = new RecordingProgress { RejectCertificate = true };

        var outcome = await robot.RunPlatformAsync(PlatformJob(request: "CSR", companyDone: false), progress, Ct);

        Assert.Equal(ArcaRobotOutcome.Stopped(ArcaRobotFailure.ScreenChanged, ArcaRobotSteps.PlatformCertificate), outcome);
    }

    // H22: a driver that went down is created again on the next pass instead of failing every pass after it.
    [Fact]
    public async Task A_driver_that_went_down_comes_back_on_the_next_pass()
    {
        Assert.Equal(ArcaRobotOutcome.Success, await robot.RunCompanyAsync(CompanyJob(), Client(), new RecordingProgress(), Ct));
        var driver = (IPlaywright)typeof(PlaywrightArcaDelegationRobot)
            .GetField("playwright", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(robot)!;
        driver.Dispose();
        web.Reset();

        Assert.Equal(ArcaRobotOutcome.Success, await robot.RunCompanyAsync(CompanyJob(), Client(), new RecordingProgress(), Ct));
    }

    [Fact]
    public async Task With_the_robot_on_and_chromium_installed_the_check_lets_the_api_start()
    {
        var on = new ArcaRobotBrowserCheck(Options.Create(new ArcaRobotBrowserOptions { Enabled = true }));
        var off = new ArcaRobotBrowserCheck(Options.Create(new ArcaRobotBrowserOptions { Enabled = false }));

        await on.StartAsync(Ct);
        await off.StartAsync(Ct);
    }

    // H21: the Api does not start with Playwright's debugging on, by any DEBUG with «*» or «pw» or by PWDEBUG: it would log
    // what is typed.
    [Theory]
    [InlineData("DEBUG", "pw:api", true)]
    [InlineData("DEBUG", "*", true)]
    [InlineData("DEBUG", "express:*", true)]
    [InlineData("PWDEBUG", "1", true)]
    [InlineData("DEBUG", "app", false)]
    public void Any_debug_with_a_star_or_pw_counts_as_playwright_debugging(string name, string value, bool on) =>
        Assert.Equal(on, ArcaRobotBrowserCheck.PlaywrightDebugging(variable => variable == name ? value : null));

    private static ArcaCompanyRobotWorkResponse CompanyJob(IReadOnlyDictionary<string, string>? outputs = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), FakeArcaWeb.Company, FakeArcaWeb.Platform, [Wsfe], [Registry, Wsfe],
            new Dictionary<string, string>(outputs ?? new Dictionary<string, string>(), StringComparer.Ordinal));

    private static ArcaPlatformRobotWorkResponse PlatformJob(string? request, bool companyDone) =>
        new(Guid.NewGuid(), Guid.NewGuid(), FakeArcaWeb.Company, FakeArcaWeb.Platform, Alias, request, [], companyDone, [Wsfe], [Registry]);

    private static ArcaFiscalCredentials Client() => new(FakeArcaWeb.ClientLogin, new ArcaFiscalKey(FakeArcaWeb.ClientKey));

    /// <summary>Lo que el robot avisó, en orden; puede perder la reserva después de un paso o rechazar el certificado.</summary>
    private sealed class RecordingProgress : IArcaRobotProgress
    {
        public List<string> Completed { get; } = [];
        public Dictionary<string, string> Outputs { get; } = new(StringComparer.Ordinal);
        public List<string> Certificates { get; } = [];
        public string? LoseAfter { get; init; }

        /// <summary>O10: el paso cuyo cierre ya no se guarda, como si la reserva se perdiera después de confirmarlo en ARCA.</summary>
        public string? LoseAtComplete { get; init; }
        public bool RejectCertificate { get; init; }

        public int Renewals => Volatile.Read(ref renewals);

        private int renewals;

        public Task<bool> RenewAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref renewals);
            return Task.FromResult(true);
        }

        public Task<bool> BeginAsync(string step, CancellationToken cancellationToken) =>
            Task.FromResult(LoseAfter is null || !Completed.Contains(LoseAfter));

        public Task<bool> RecordAsync(string step, string output, CancellationToken cancellationToken)
        {
            Outputs[step] = output;
            return Task.FromResult(true);
        }

        public Task<bool> CompleteAsync(string step, string? output, CancellationToken cancellationToken)
        {
            if (step == LoseAtComplete) return Task.FromResult(false);
            Completed.Add(step);
            if (output is not null) Outputs[step] = output;
            return Task.FromResult(true);
        }

        public Task<bool> InstallCertificateAsync(string certificatePem, CancellationToken cancellationToken)
        {
            Certificates.Add(certificatePem);
            return Task.FromResult(!RejectCertificate);
        }
    }
}
