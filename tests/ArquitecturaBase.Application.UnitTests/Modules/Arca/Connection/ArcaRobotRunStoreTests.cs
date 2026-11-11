using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Application.UnitTests.Modules.Arca.Connection;

public sealed class ArcaRobotRunStoreTests
{
    private const string Registry = "ws_sr_constancia_inscripcion";
    private const string Invoicing = "wsfe";
    private static readonly string AuthorizeRegistry = ArcaRobotSteps.PlatformAuthorize(ArcaServiceCode.Create(Registry)!.Value);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Steps_are_saved_only_with_the_own_lease()
    {
        using var host = new ArcaApplicationHost(withRobot: true);
        var (run, owner) = await StartCompanyAsync(host);

        Assert.True(await host.RobotStore.BeginStepAsync(run, owner, ArcaRobotSteps.CompanyLogin, Ct));
        Assert.False(await host.RobotStore.CompleteStepAsync(run, Guid.NewGuid(), ArcaRobotSteps.CompanyLogin, null, Ct));
        Assert.True(await host.RobotStore.CompleteStepAsync(run, owner, "company.point-of-sale", "7", Ct));

        Assert.Equal(["company.point-of-sale"], host.Runs.Company!.CompletedSteps);
        Assert.Equal("7", (await host.Robot.GetAsync(Ct)).Value.Company!.Outputs["company.point-of-sale"]);
    }

    [Fact]
    public async Task A_finished_company_side_wakes_the_platform_side()
    {
        using var host = new ArcaApplicationHost(withRobot: true);
        var (run, owner) = await StartCompanyAsync(host);
        var platformOwner = Guid.NewGuid();
        await host.RobotStore.ReservePlatformAsync(host.Runs.Platform!.Id, platformOwner, Ct);
        await host.RobotStore.FinishPlatformAsync(host.Runs.Platform.Id, platformOwner, ArcaRobotOutcome.Success, null, Ct);
        Assert.Equal(host.Clock.GetUtcNow().UtcDateTime.AddHours(1), host.Runs.Platform.NextAttemptAtUtc);

        Assert.True(await host.RobotStore.FinishCompanyAsync(run, owner, ArcaRobotOutcome.Success, Ct));

        Assert.Equal(ArcaRobotRunStatus.Succeeded, host.Runs.Company!.Status);
        Assert.Equal(host.Clock.GetUtcNow().UtcDateTime, host.Runs.Platform.NextAttemptAtUtc);
    }

    [Fact]
    public async Task A_company_finished_by_hand_after_a_stop_lets_the_platform_accept_and_assign_at_once()
    {
        using var host = new ArcaApplicationHost(withRobot: true);
        var (run, owner) = await StartCompanyAsync(host);
        var platform = host.Runs.Platform!;
        var platformOwner = Guid.NewGuid();
        await host.RobotStore.ReservePlatformAsync(platform.Id, platformOwner, Ct);
        await host.RobotStore.FinishPlatformAsync(platform.Id, platformOwner, ArcaRobotOutcome.Success, null, Ct);
        Assert.Equal(host.Clock.GetUtcNow().UtcDateTime.AddHours(1), platform.NextAttemptAtUtc);
        Assert.Equal(ArcaConnectionErrors.RobotNotStopped, (await host.RobotStore.CompleteCompanyByHandAsync(Ct)).Error);
        await host.RobotStore.FinishCompanyAsync(run, owner, ArcaRobotOutcome.Stopped(ArcaRobotFailure.Captcha, ArcaRobotSteps.CompanyLogin), Ct);

        Assert.True((await host.RobotStore.CompleteCompanyByHandAsync(Ct)).IsSuccess);

        Assert.Equal(ArcaRobotRunStatus.Succeeded, host.Runs.Company!.Status);
        var job = await host.RobotStore.ReservePlatformAsync(platform.Id, Guid.NewGuid(), Ct);
        Assert.True(job!.CompanyDone);
    }

    [Fact]
    public async Task Without_a_company_side_there_is_nothing_to_finish_by_hand()
    {
        using var host = new ArcaApplicationHost(withRobot: true);
        await host.StartAutomatedAsync(Invoicing);

        Assert.Equal(ArcaConnectionErrors.RobotNotStopped, (await host.RobotStore.CompleteCompanyByHandAsync(Ct)).Error);
        Assert.Null(host.Runs.Company);
    }

    [Fact]
    public async Task A_screen_change_on_the_company_side_alerts_the_owner_once_a_day()
    {
        using var host = new ArcaApplicationHost(withRobot: true);
        var (run, owner) = await StartCompanyAsync(host);
        var stopped = ArcaRobotOutcome.Stopped(ArcaRobotFailure.ScreenChanged, "company.delegate.wsfe");

        Assert.True(await host.RobotStore.FinishCompanyAsync(run, owner, stopped, Ct));

        var alert = Assert.Single(host.Emails.Messages);
        Assert.Equal(ArcaApplicationHost.AlertEmail, alert.To);
        Assert.Contains("company.delegate.wsfe", alert.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("20000000060", alert.TextBody, StringComparison.Ordinal);
        Assert.Equal(ArcaRobotFailure.ScreenChanged, host.Runs.Company!.Failure);

        var (again, againOwner) = await RestartCompanyAsync(host);
        await host.RobotStore.FinishCompanyAsync(again, againOwner, stopped, Ct);
        Assert.Single(host.Emails.Messages);

        host.Clock.Advance(TimeSpan.FromDays(1));
        (again, againOwner) = await RestartCompanyAsync(host);
        await host.RobotStore.FinishCompanyAsync(again, againOwner, stopped, Ct);
        Assert.Equal(2, host.Emails.Messages.Count);
    }

    [Fact]
    public async Task A_rejected_company_login_or_arca_not_answering_does_not_alert()
    {
        using var host = new ArcaApplicationHost(withRobot: true);
        var (run, owner) = await StartCompanyAsync(host);
        await host.RobotStore.FinishCompanyAsync(run, owner, ArcaRobotOutcome.Stopped(ArcaRobotFailure.LoginRejected, ArcaRobotSteps.CompanyLogin), Ct);
        var (again, againOwner) = await RestartCompanyAsync(host);
        await host.RobotStore.FinishCompanyAsync(again, againOwner, ArcaRobotOutcome.Stopped(ArcaRobotFailure.Unavailable, ArcaRobotSteps.CompanyLogin), Ct);

        Assert.Empty(host.Emails.Messages);
        Assert.Equal(ArcaRobotFailure.Unavailable, host.Runs.Company!.Failure);
    }

    [Fact]
    public async Task Without_an_alert_address_only_the_log_remains()
    {
        using var host = new ArcaApplicationHost(withRobot: true, alertEmail: null);
        var (run, owner) = await StartCompanyAsync(host);

        await host.RobotStore.FinishCompanyAsync(run, owner, ArcaRobotOutcome.Stopped(ArcaRobotFailure.ScreenChanged, ArcaRobotSteps.CompanyLogin), Ct);

        Assert.Empty(host.Emails.Messages);
        Assert.Null(host.Runs.Company!.AlertedAtUtc);
    }

    [Fact]
    public async Task Reserving_the_platform_side_hands_the_pending_request_the_alias_and_the_services()
    {
        using var host = new ArcaApplicationHost(withRobot: true);
        await StartCompanyAsync(host);
        var owner = Guid.NewGuid();

        var job = await host.RobotStore.ReservePlatformAsync(host.Runs.Platform!.Id, owner, Ct);

        Assert.NotNull(job);
        Assert.Equal("CSR:plataforma-demo", job.CertificateRequestPem);
        Assert.Equal("plataforma-demo", job.Alias);
        Assert.Equal(ArcaApplicationHost.PlatformCuit, job.PlatformCuit);
        Assert.Equal([Invoicing], job.DelegatedServices);
        Assert.Equal([Registry], job.AuthorizedServices);
        Assert.False(job.CompanyDone);
        Assert.Null(await host.RobotStore.ReservePlatformAsync(host.Runs.Platform.Id, Guid.NewGuid(), Ct));
    }

    [Fact]
    public async Task The_platform_installs_its_certificate_through_the_usual_upload()
    {
        using var host = new ArcaApplicationHost(withRobot: true);
        await StartCompanyAsync(host);
        var owner = Guid.NewGuid();
        await host.RobotStore.ReservePlatformAsync(host.Runs.Platform!.Id, owner, Ct);

        Assert.False(await host.RobotStore.InstallPlatformCertificateAsync(host.Runs.Platform.Id, Guid.NewGuid(), "CERT", Ct));
        Assert.True(await host.RobotStore.InstallPlatformCertificateAsync(host.Runs.Platform.Id, owner, "CERT", Ct));

        Assert.Equal([ArcaApplicationHost.PlatformCuit], host.Credentials.InspectedCuits);
        Assert.Equal(ArcaConnectionStatus.Unverified, host.Store.Connection!.Status);
        Assert.True(host.Store.Certificate(ArcaCertificateRole.Active)!.IsLoaded);
    }

    [Fact]
    public async Task With_nothing_to_do_before_the_company_the_platform_waits_without_entering_arca()
    {
        using var host = new ArcaApplicationHost(withRobot: true);
        await StartCompanyAsync(host);
        var owner = Guid.NewGuid();
        var platform = host.Runs.Platform!;
        await host.RobotStore.ReservePlatformAsync(platform.Id, owner, Ct);
        await host.RobotStore.InstallPlatformCertificateAsync(platform.Id, owner, "CERT", Ct);
        await host.RobotStore.CompleteStepAsync(platform.Id, owner, AuthorizeRegistry, null, Ct);
        await host.RobotStore.FinishPlatformAsync(platform.Id, owner, ArcaRobotOutcome.Success, null, Ct);
        host.Clock.Advance(TimeSpan.FromHours(1));

        Assert.Null(await host.RobotStore.ReservePlatformAsync(platform.Id, Guid.NewGuid(), Ct));

        Assert.Equal(ArcaRobotRunStatus.Waiting, platform.Status);
        Assert.Equal(host.Clock.GetUtcNow().UtcDateTime.AddHours(1), platform.NextAttemptAtUtc);
    }

    [Fact]
    public async Task Arca_not_answering_postpones_the_platform_side_with_a_growing_wait_until_the_attempts_run_out()
    {
        using var host = new ArcaApplicationHost(withRobot: true);
        await StartCompanyAsync(host);
        var platform = host.Runs.Platform!;
        var unavailable = ArcaRobotOutcome.Stopped(ArcaRobotFailure.Unavailable, ArcaRobotSteps.PlatformLogin);

        var waits = new List<TimeSpan>();
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var owner = Guid.NewGuid();
            Assert.NotNull(await host.RobotStore.ReservePlatformAsync(platform.Id, owner, Ct));
            await host.RobotStore.FinishPlatformAsync(platform.Id, owner, unavailable, null, Ct);
            if (platform.NextAttemptAtUtc is { } next) { waits.Add(next - host.Clock.GetUtcNow().UtcDateTime); host.Clock.Advance(waits[^1]); }
        }

        Assert.Equal([TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(8), TimeSpan.FromMinutes(16)], waits);
        Assert.Equal(ArcaRobotRunStatus.Failed, platform.Status);
        Assert.Equal(ArcaRobotFailure.Unavailable, platform.Failure);
        Assert.Empty(host.Emails.Messages);
    }

    [Fact]
    public async Task A_rejected_platform_login_fails_at_once_and_alerts()
    {
        using var host = new ArcaApplicationHost(withRobot: true);
        await StartCompanyAsync(host);
        var owner = Guid.NewGuid();
        await host.RobotStore.ReservePlatformAsync(host.Runs.Platform!.Id, owner, Ct);

        await host.RobotStore.FinishPlatformAsync(host.Runs.Platform.Id, owner,
            ArcaRobotOutcome.Stopped(ArcaRobotFailure.LoginRejected, ArcaRobotSteps.PlatformLogin), null, Ct);

        Assert.Equal(ArcaRobotRunStatus.Failed, host.Runs.Platform.Status);
        Assert.Contains(ArcaRobotSteps.PlatformLogin, Assert.Single(host.Emails.Messages).TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_company_that_finished_during_the_platform_pass_makes_it_due_at_once()
    {
        using var host = new ArcaApplicationHost(withRobot: true);
        var (company, companyOwner) = await StartCompanyAsync(host);
        var platform = host.Runs.Platform!;
        var owner = Guid.NewGuid();
        await host.RobotStore.ReservePlatformAsync(platform.Id, owner, Ct);
        await host.RobotStore.FinishCompanyAsync(company, companyOwner, ArcaRobotOutcome.Success, Ct);

        await host.RobotStore.FinishPlatformAsync(platform.Id, owner, ArcaRobotOutcome.Success, null, Ct);

        Assert.Equal(ArcaRobotRunStatus.Waiting, platform.Status);
        Assert.Equal(host.Clock.GetUtcNow().UtcDateTime, platform.NextAttemptAtUtc);
    }

    [Fact]
    public async Task With_the_company_done_the_connection_test_confirms_and_finishes()
    {
        using var host = new ArcaApplicationHost(withRobot: true);
        var (company, companyOwner) = await StartCompanyAsync(host);
        var platform = host.Runs.Platform!;
        var owner = Guid.NewGuid();
        await host.RobotStore.ReservePlatformAsync(platform.Id, owner, Ct);
        await host.RobotStore.InstallPlatformCertificateAsync(platform.Id, owner, "CERT", Ct);
        await host.RobotStore.FinishCompanyAsync(company, companyOwner, ArcaRobotOutcome.Success, Ct);

        Assert.True(await host.RobotStore.FinishPlatformAsync(platform.Id, owner, ArcaRobotOutcome.Success, await host.Test.TestAsync(Ct), Ct));

        Assert.Equal(ArcaRobotRunStatus.Succeeded, platform.Status);
        Assert.Contains(ArcaRobotSteps.Verify, platform.CompletedSteps);
        Assert.True(host.Store.Connection!.LastTestPassed);
    }

    [Fact]
    public async Task A_delegation_arca_does_not_reflect_waits_and_after_the_attempts_alerts()
    {
        using var host = new ArcaApplicationHost(withRobot: true);
        var (company, companyOwner) = await StartCompanyAsync(host);
        var platform = host.Runs.Platform!;
        var first = Guid.NewGuid();
        await host.RobotStore.ReservePlatformAsync(platform.Id, first, Ct);
        await host.RobotStore.InstallPlatformCertificateAsync(platform.Id, first, "CERT", Ct);
        await host.RobotStore.FinishCompanyAsync(company, companyOwner, ArcaRobotOutcome.Success, Ct);
        host.InvoicingFamily.Status = ArcaServiceCheckStatus.NotDelegated;

        await host.RobotStore.FinishPlatformAsync(platform.Id, first, ArcaRobotOutcome.Success, await host.Test.TestAsync(Ct), Ct);
        Assert.Equal(ArcaRobotRunStatus.Waiting, platform.Status);
        Assert.Null(platform.Failure);

        // El primer intento ya pasó; los cuatro que quedan esperan, y el último se da por fallido.
        for (var attempt = 2; attempt <= 5; attempt++)
        {
            host.Clock.Advance(TimeSpan.FromHours(1));
            var owner = Guid.NewGuid();
            Assert.NotNull(await host.RobotStore.ReservePlatformAsync(platform.Id, owner, Ct));
            await host.RobotStore.FinishPlatformAsync(platform.Id, owner, ArcaRobotOutcome.Success, await host.Test.TestAsync(Ct), Ct);
        }

        Assert.Equal(ArcaRobotRunStatus.Failed, platform.Status);
        Assert.Equal(ArcaRobotFailure.ScreenChanged, platform.Failure);
        Assert.Equal(ArcaRobotSteps.Verify, platform.FailedStep);
        Assert.Single(host.Emails.Messages);
    }

    private static async Task<(Guid Run, Guid Owner)> StartCompanyAsync(ArcaApplicationHost host)
    {
        var started = await host.StartAutomatedAsync(Invoicing);
        Assert.True((await host.Robot.StartAsync(new ArcaRobotStartRequest(true, "20-28456789-8", new ArcaFiscalKey("clave"), started.Revision), Ct)).IsSuccess);
        var job = host.RobotQueue.Jobs[^1].Job;
        return (job.RunId, job.Owner);
    }

    private static async Task<(Guid Run, Guid Owner)> RestartCompanyAsync(ArcaApplicationHost host)
    {
        var revision = (await host.Connection.GetAsync(Ct)).Value.Revision;
        Assert.True((await host.Robot.StartAsync(new ArcaRobotStartRequest(true, "20-28456789-8", new ArcaFiscalKey("clave"), revision), Ct)).IsSuccess);
        var job = host.RobotQueue.Jobs[^1].Job;
        return (job.RunId, job.Owner);
    }
}
