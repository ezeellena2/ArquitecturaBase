using System.Text.Json;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Application.UnitTests.Modules.Arca.Connection;

public sealed class ArcaRobotServiceTests
{
    private const string Registry = "ws_sr_constancia_inscripcion";
    private const string Invoicing = "wsfe";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_automated_alta_needs_the_robot_turned_on()
    {
        using var off = new ArcaApplicationHost(withPlatform: true);
        var refused = await off.Connection.StartCertificateRequestAsync("20-00000006-0", ArcaEnvironment.Production, "plataforma-demo", [Invoicing], 0,
            ArcaAccessMode.PlatformDelegationAutomated, Ct);
        Assert.Equal(ArcaConnectionErrors.ModeUnavailable, refused.Error);

        using var on = new ArcaApplicationHost(withRobot: true);
        var started = await on.StartAutomatedAsync(Invoicing);

        Assert.Equal(ArcaAccessMode.PlatformDelegationAutomated, started.Mode);
        Assert.Equal(ArcaApplicationHost.PlatformCuit, started.PlatformCuit);
        Assert.Equal([ArcaApplicationHost.PlatformCuit], on.Credentials.RequestedCuits);
    }

    [Fact]
    public async Task Starting_is_refused_while_the_robot_is_off()
    {
        using var host = new ArcaApplicationHost(withPlatform: true);

        var result = await host.Robot.StartAsync(Request(1), Ct);

        Assert.Equal(ArcaConnectionErrors.RobotDisabled, result.Error);
        Assert.Equal(0, host.Work.Transactions);
        Assert.False((await host.Robot.GetAsync(Ct)).Value.Enabled);
    }

    [Fact]
    public async Task Finishing_by_hand_is_refused_while_the_robot_is_off()
    {
        using var host = new ArcaApplicationHost(withPlatform: true);

        var result = await host.Robot.CompleteByHandAsync(Ct);

        Assert.Equal(ArcaConnectionErrors.RobotDisabled, result.Error);
        Assert.Equal(0, host.Work.Transactions);
    }

    [Fact]
    public async Task The_consent_the_login_and_the_key_are_checked_before_the_transaction()
    {
        using var host = new ArcaApplicationHost(withRobot: true);

        Assert.Equal(ArcaConnectionErrors.RobotConsentMissing, (await host.Robot.StartAsync(Request(1) with { Consent = false }, Ct)).Error);
        Assert.Equal(ArcaConnectionErrors.CuitInvalid, (await host.Robot.StartAsync(Request(1) with { LoginCuit = "20-28456789-0" }, Ct)).Error);
        Assert.Equal(ArcaConnectionErrors.RobotKeyMissing, (await host.Robot.StartAsync(Request(1) with { FiscalKey = new ArcaFiscalKey(" ") }, Ct)).Error);
        Assert.Equal(0, host.Work.Transactions);
        Assert.Empty(host.RobotQueue.Jobs);
    }

    [Fact]
    public async Task Only_a_connection_in_the_automated_mode_and_with_the_seen_revision_starts()
    {
        using var host = new ArcaApplicationHost(withRobot: true);
        Assert.Equal(ArcaConnectionErrors.NotConnected, (await host.Robot.StartAsync(Request(0), Ct)).Error);

        var guided = await host.Connection.StartCertificateRequestAsync("20-00000006-0", ArcaEnvironment.Production, "plataforma-demo", [Invoicing], 0,
            ArcaAccessMode.PlatformDelegation, Ct);
        Assert.Equal(ArcaConnectionErrors.RobotModeMismatch, (await host.Robot.StartAsync(Request(guided.Value.Revision), Ct)).Error);

        using var automated = new ArcaApplicationHost(withRobot: true);
        var started = await automated.StartAutomatedAsync(Invoicing);
        Assert.Equal(ArcaConnectionErrors.RevisionConflict, (await automated.Robot.StartAsync(Request(started.Revision + 1), Ct)).Error);
        Assert.Empty(automated.Runs.Runs);
    }

    [Fact]
    public async Task Starting_reserves_the_company_side_waits_the_platform_side_and_hands_the_key_after_the_commit()
    {
        using var host = new ArcaApplicationHost(withRobot: true);
        var started = await host.StartAutomatedAsync(Invoicing);
        var key = new ArcaFiscalKey("clave-de-prueba");
        var committed = false;
        host.RobotQueue.WhenEnqueued = () => committed = !host.Work.InTransaction;

        var result = await host.Robot.StartAsync(new ArcaRobotStartRequest(true, "20-28456789-8", key, started.Revision), Ct);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Enabled);
        Assert.Equal(ArcaRobotRunState.Running, result.Value.Company!.State);
        Assert.Equal(ArcaRobotRunState.Waiting, result.Value.Platform!.State);
        Assert.True(committed);
        var (job, credentials) = Assert.Single(host.RobotQueue.Jobs);
        Assert.Equal(host.Runs.Company!.Id, job.RunId);
        Assert.Equal(host.Runs.Company.LeaseOwner, job.Owner);
        Assert.Equal("20000000060", job.CompanyCuit);
        Assert.Equal(ArcaApplicationHost.PlatformCuit, job.PlatformCuit);
        Assert.Equal([Invoicing], job.DelegatedServices);
        Assert.Equal([Registry, Invoicing], job.EnabledServices);
        Assert.Equal("20284567898", credentials.LoginCuit);
        Assert.Same(key, credentials.Key);
        Assert.Equal(host.User.UserId, host.Runs.Company.ConsentedBy);
    }

    [Fact]
    public async Task A_company_side_that_is_running_cannot_start_again()
    {
        using var host = new ArcaApplicationHost(withRobot: true);
        var started = await host.StartAutomatedAsync(Invoicing);
        await host.Robot.StartAsync(Request(started.Revision), Ct);

        var again = await host.Robot.StartAsync(Request(started.Revision), Ct);

        Assert.Equal(ArcaConnectionErrors.RobotRunInProgress, again.Error);
        Assert.Single(host.RobotQueue.Jobs);
    }

    [Fact]
    public async Task When_the_host_cannot_take_the_run_it_fails_and_the_key_is_dropped()
    {
        using var host = new ArcaApplicationHost(withRobot: true);
        var started = await host.StartAutomatedAsync(Invoicing);
        host.RobotQueue.Accepts = false;

        var result = await host.Robot.StartAsync(Request(started.Revision), Ct);

        Assert.Equal(ArcaConnectionErrors.RobotBusy, result.Error);
        Assert.Equal(ArcaRobotRunStatus.Failed, host.Runs.Company!.Status);
        Assert.Equal(ArcaRobotFailure.Unavailable, host.Runs.Company.Failure);
        Assert.Empty(host.Emails.Messages);
    }

    [Fact]
    public async Task A_company_side_whose_host_died_reads_as_interrupted_and_can_start_again()
    {
        using var host = new ArcaApplicationHost(withRobot: true);
        var started = await host.StartAutomatedAsync(Invoicing);
        await host.Robot.StartAsync(Request(started.Revision), Ct);
        await host.RobotStore.BeginStepAsync(host.Runs.Company!.Id, host.Runs.Company.LeaseOwner!.Value, ArcaRobotSteps.CompanyLogin, Ct);
        host.Clock.Advance(TimeSpan.FromMinutes(3));

        var read = (await host.Robot.GetAsync(Ct)).Value.Company!;
        Assert.Equal(ArcaRobotRunState.Interrupted, read.State);
        Assert.Equal(ArcaRobotFailure.Interrupted, read.Failure);
        Assert.Equal(ArcaRobotSteps.CompanyLogin, read.FailedStep);

        Assert.True((await host.Robot.StartAsync(Request(started.Revision), Ct)).IsSuccess);
        Assert.Equal(2, host.RobotQueue.Jobs.Count);
    }

    [Fact]
    public void The_key_never_prints_nor_serializes()
    {
        var key = new ArcaFiscalKey("clave-de-prueba");
        var credentials = new ArcaFiscalCredentials("20284567898", key);
        var request = new ArcaRobotStartRequest(true, "20-28456789-8", key, 3);

        Assert.DoesNotContain("clave-de-prueba", key.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("clave-de-prueba", credentials.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("28456789", credentials.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("clave-de-prueba", request.ToString(), StringComparison.Ordinal);
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Serialize(key));
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Serialize(credentials));
    }

    private static ArcaRobotStartRequest Request(long revision) => new(true, "20-28456789-8", new ArcaFiscalKey("clave-de-prueba"), revision);
}
