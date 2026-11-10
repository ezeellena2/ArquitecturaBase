using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.Arca;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Infrastructure.Modules.Arca.Connection.Robot;
using ArquitecturaBase.Infrastructure.Modules.Arca.Invoicing.Robot;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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
        }), NullLogger<PlaywrightArcaDelegationRobot>.Instance);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => robot.DisposeAsync();

    [Fact]
    public async Task The_company_side_delegates_each_service_and_adds_a_web_service_point_of_sale()
    {
        var progress = new RecordingProgress();

        var outcome = await robot.RunCompanyAsync(CompanyJob(), Client(), progress, Ct);

        Assert.Equal(ArcaRobotOutcome.Success, outcome);
        Assert.Equal(["company.login", "company.delegate.wsfe", "company.point-of-sale"], progress.Completed);
        Assert.Equal("2", progress.Outputs["company.point-of-sale"]);
        var relation = Assert.Single(web.Relations);
        Assert.Equal(new FakeArcaWebRelation(FakeArcaWeb.Company, Wsfe, FakeArcaWeb.Platform, Accepted: false), relation);
        Assert.Contains(web.Points, point => point is { Owner: FakeArcaWeb.Company, Number: 2, Dropped: false } && point.System.EndsWith("Web Services", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Running_the_company_side_again_duplicates_nothing()
    {
        await robot.RunCompanyAsync(CompanyJob(), Client(), new RecordingProgress(), Ct);
        var again = new RecordingProgress();

        Assert.Equal(ArcaRobotOutcome.Success, await robot.RunCompanyAsync(CompanyJob(), Client(), again, Ct));

        Assert.Single(web.Relations);
        Assert.Equal(2, web.Points.Count(point => point.Owner == FakeArcaWeb.Company));
        Assert.Equal("2", again.Outputs["company.point-of-sale"]);
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

    [Fact]
    public async Task With_the_robot_on_and_chromium_installed_the_check_lets_the_api_start()
    {
        var on = new ArcaRobotBrowserCheck(Options.Create(new ArcaRobotBrowserOptions { Enabled = true }));
        var off = new ArcaRobotBrowserCheck(Options.Create(new ArcaRobotBrowserOptions { Enabled = false }));

        await on.StartAsync(Ct);
        await off.StartAsync(Ct);
    }

    private static ArcaCompanyRobotWorkResponse CompanyJob() =>
        new(Guid.NewGuid(), Guid.NewGuid(), FakeArcaWeb.Company, FakeArcaWeb.Platform, [Wsfe], [Registry, Wsfe]);

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
        public bool RejectCertificate { get; init; }

        public Task<bool> BeginAsync(string step, CancellationToken cancellationToken) =>
            Task.FromResult(LoseAfter is null || !Completed.Contains(LoseAfter));

        public Task<bool> CompleteAsync(string step, string? output, CancellationToken cancellationToken)
        {
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
