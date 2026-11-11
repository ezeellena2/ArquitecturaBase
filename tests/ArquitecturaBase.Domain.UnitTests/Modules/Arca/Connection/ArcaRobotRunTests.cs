using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Domain.UnitTests.Modules.Arca.Connection;

public sealed class ArcaRobotRunTests
{
    private static readonly DateTime Now = new(2026, 11, 9, 16, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(3);
    private static readonly Guid ConnectionId = Guid.CreateVersion7();
    private static readonly Guid Operator = Guid.CreateVersion7();
    private static readonly Guid Owner = Guid.CreateVersion7();

    [Fact]
    public void The_company_side_starts_running_with_the_consent_and_a_lease()
    {
        var run = ArcaRobotRun.StartCompany(ConnectionId, Operator, Owner, Now, Lease);

        Assert.Equal(ArcaRobotSide.Company, run.Side);
        Assert.Equal(ArcaRobotRunStatus.Running, run.Status);
        Assert.Equal(Operator, run.ConsentedBy);
        Assert.Equal(Now, run.ConsentedAtUtc);
        Assert.Equal(Now, run.StartedAtUtc);
        Assert.True(run.IsHeld(Now.AddMinutes(2)));
        Assert.False(run.IsHeld(Now.Add(Lease)));
        Assert.Empty(run.CompletedSteps);
    }

    [Fact]
    public void Each_step_renews_the_lease_and_is_recorded_once_with_its_output()
    {
        var run = ArcaRobotRun.StartCompany(ConnectionId, Operator, Owner, Now, Lease);

        Assert.True(run.Begin(ArcaRobotSteps.CompanyLogin, Owner, Now.AddMinutes(2), Lease));
        Assert.Equal(ArcaRobotSteps.CompanyLogin, run.CurrentStep);
        Assert.True(run.IsHeld(Now.AddMinutes(4)));

        Assert.True(run.Complete(ArcaRobotSteps.CompanyLogin, null, Owner, Now.AddMinutes(3), Lease));
        Assert.True(run.Complete("company.point-of-sale", "5", Owner, Now.AddMinutes(3), Lease));
        Assert.True(run.Complete(ArcaRobotSteps.CompanyLogin, null, Owner, Now.AddMinutes(3), Lease));

        Assert.Null(run.CurrentStep);
        Assert.Equal([ArcaRobotSteps.CompanyLogin, "company.point-of-sale"], run.CompletedSteps);
        Assert.Equal("5", run.Outputs["company.point-of-sale"]);
    }

    [Fact]
    public void Another_owner_or_an_expired_lease_cannot_write()
    {
        var run = ArcaRobotRun.StartCompany(ConnectionId, Operator, Owner, Now, Lease);

        Assert.False(run.Begin(ArcaRobotSteps.CompanyLogin, Guid.CreateVersion7(), Now, Lease));
        Assert.False(run.Complete(ArcaRobotSteps.CompanyLogin, null, Owner, Now.Add(Lease), Lease));
        Assert.False(run.Succeed(Owner, Now.Add(Lease)));
        Assert.Empty(run.CompletedSteps);
        Assert.Equal(ArcaRobotRunStatus.Running, run.Status);
    }

    [Fact]
    public void A_running_side_whose_lease_expired_is_interrupted()
    {
        var run = ArcaRobotRun.StartCompany(ConnectionId, Operator, Owner, Now, Lease);

        Assert.False(run.IsInterrupted(Now.AddMinutes(1)));
        Assert.True(run.IsInterrupted(Now.Add(Lease)));
    }

    [Fact]
    public void Failing_keeps_the_reason_and_the_step_and_releases_the_lease()
    {
        var run = ArcaRobotRun.StartCompany(ConnectionId, Operator, Owner, Now, Lease);
        run.Complete(ArcaRobotSteps.CompanyLogin, null, Owner, Now, Lease);

        Assert.True(run.Fail(ArcaRobotFailure.ScreenChanged, "company.point-of-sale", Owner, Now.AddMinutes(1)));

        Assert.Equal(ArcaRobotRunStatus.Failed, run.Status);
        Assert.Equal(ArcaRobotFailure.ScreenChanged, run.Failure);
        Assert.Equal("company.point-of-sale", run.FailedStep);
        Assert.Equal(Now.AddMinutes(1), run.FinishedAtUtc);
        Assert.False(run.IsHeld(Now.AddMinutes(1)));
        Assert.Equal([ArcaRobotSteps.CompanyLogin], run.CompletedSteps);
    }

    [Fact]
    public void Succeeding_finishes_the_run()
    {
        var run = ArcaRobotRun.StartCompany(ConnectionId, Operator, Owner, Now, Lease);

        Assert.True(run.Succeed(Owner, Now.AddMinutes(1)));

        Assert.Equal(ArcaRobotRunStatus.Succeeded, run.Status);
        Assert.Equal(Now.AddMinutes(1), run.FinishedAtUtc);
        Assert.False(run.IsHeld(Now.AddMinutes(1)));
    }

    [Fact]
    public void Restarting_the_company_side_is_refused_while_it_runs_and_starts_over_afterwards()
    {
        var run = ArcaRobotRun.StartCompany(ConnectionId, Operator, Owner, Now, Lease);
        Assert.Equal(ArcaConnectionErrors.RobotRunInProgress, run.RestartCompany(Operator, Guid.CreateVersion7(), Now.AddMinutes(1), Lease).Error);

        run.Complete(ArcaRobotSteps.CompanyLogin, null, Owner, Now, Lease);
        run.Fail(ArcaRobotFailure.Captcha, ArcaRobotSteps.CompanyLogin, Owner, Now.AddMinutes(1));
        var other = Guid.CreateVersion7();
        var newOwner = Guid.CreateVersion7();

        Assert.True(run.RestartCompany(other, newOwner, Now.AddMinutes(2), Lease).IsSuccess);

        Assert.Equal(ArcaRobotRunStatus.Running, run.Status);
        Assert.Equal(other, run.ConsentedBy);
        Assert.Equal(Now.AddMinutes(2), run.StartedAtUtc);
        Assert.Null(run.Failure);
        Assert.Null(run.FailedStep);
        Assert.Null(run.FinishedAtUtc);
        Assert.Empty(run.CompletedSteps);
        Assert.True(run.Begin(ArcaRobotSteps.CompanyLogin, newOwner, Now.AddMinutes(2), Lease));
    }

    [Fact]
    public void A_stopped_company_side_is_finished_by_hand_but_not_while_it_runs()
    {
        var run = ArcaRobotRun.StartCompany(ConnectionId, Operator, Owner, Now, Lease);
        Assert.Equal(ArcaConnectionErrors.RobotNotStopped, run.CompleteByHand(Now.AddMinutes(1)).Error);

        run.Complete(ArcaRobotSteps.CompanyLogin, null, Owner, Now, Lease);
        run.Fail(ArcaRobotFailure.Captcha, ArcaRobotSteps.CompanyLogin, Owner, Now.AddMinutes(1));

        Assert.True(run.CompleteByHand(Now.AddMinutes(5)).IsSuccess);

        Assert.Equal(ArcaRobotRunStatus.Succeeded, run.Status);
        Assert.Null(run.Failure);
        Assert.Null(run.FailedStep);
        Assert.Equal(Now.AddMinutes(5), run.FinishedAtUtc);
        Assert.Equal([ArcaRobotSteps.CompanyLogin], run.CompletedSteps);
    }

    [Fact]
    public void An_interrupted_company_side_is_also_finished_by_hand()
    {
        var run = ArcaRobotRun.StartCompany(ConnectionId, Operator, Owner, Now, Lease);

        Assert.True(run.CompleteByHand(Now.Add(Lease)).IsSuccess);

        Assert.Equal(ArcaRobotRunStatus.Succeeded, run.Status);
        Assert.False(run.IsInterrupted(Now.Add(Lease)));
    }

    [Fact]
    public void An_interrupted_company_side_can_start_again()
    {
        var run = ArcaRobotRun.StartCompany(ConnectionId, Operator, Owner, Now, Lease);

        Assert.True(run.RestartCompany(Operator, Guid.CreateVersion7(), Now.Add(Lease), Lease).IsSuccess);
    }

    [Fact]
    public void The_platform_side_waits_until_due_and_one_owner_reserves_it()
    {
        var run = ArcaRobotRun.SchedulePlatform(ConnectionId, Now);

        Assert.Equal(ArcaRobotSide.Platform, run.Side);
        Assert.Equal(ArcaRobotRunStatus.Waiting, run.Status);
        Assert.True(run.TryReserve(Owner, Now, Lease));
        Assert.Equal(1, run.Attempts);
        Assert.False(run.TryReserve(Guid.CreateVersion7(), Now.AddMinutes(1), Lease));
        Assert.True(run.IsHeld(Now.AddMinutes(1)));
    }

    [Fact]
    public void A_postponed_platform_side_is_not_due_before_its_time()
    {
        var run = ArcaRobotRun.SchedulePlatform(ConnectionId, Now);
        run.TryReserve(Owner, Now, Lease);

        Assert.True(run.Postpone(Owner, ArcaRobotFailure.Unavailable, ArcaRobotSteps.PlatformAccept, Now.AddMinutes(5)));

        Assert.Equal(ArcaRobotRunStatus.Waiting, run.Status);
        Assert.Equal(ArcaRobotFailure.Unavailable, run.Failure);
        Assert.Equal(Now.AddMinutes(5), run.NextAttemptAtUtc);
        Assert.False(run.TryReserve(Owner, Now.AddMinutes(4), Lease));
        Assert.True(run.TryReserve(Owner, Now.AddMinutes(5), Lease));
        Assert.Equal(2, run.Attempts);
        Assert.Null(run.Failure);
    }

    [Fact]
    public void A_platform_side_held_by_a_host_that_died_can_be_reserved_again()
    {
        var run = ArcaRobotRun.SchedulePlatform(ConnectionId, Now);
        run.TryReserve(Owner, Now, Lease);

        Assert.True(run.TryReserve(Guid.CreateVersion7(), Now.Add(Lease), Lease));
    }

    [Fact]
    public void Waking_a_waiting_platform_side_makes_it_due_now()
    {
        var run = ArcaRobotRun.SchedulePlatform(ConnectionId, Now);
        run.TryReserve(Owner, Now, Lease);
        run.Postpone(Owner, null, null, Now.AddHours(1));

        run.Wake(Now.AddMinutes(1));

        Assert.True(run.TryReserve(Owner, Now.AddMinutes(1), Lease));
        Assert.Equal(1, run.Attempts);
    }

    [Fact]
    public void Rescheduling_starts_the_platform_side_over_unless_it_succeeded()
    {
        var failed = ArcaRobotRun.SchedulePlatform(ConnectionId, Now);
        failed.TryReserve(Owner, Now, Lease);
        failed.Complete(ArcaRobotSteps.PlatformCertificate, null, Owner, Now, Lease);
        failed.Fail(ArcaRobotFailure.LoginRejected, ArcaRobotSteps.PlatformLogin, Owner, Now);

        failed.Reschedule(Now.AddMinutes(1));

        Assert.Equal(ArcaRobotRunStatus.Waiting, failed.Status);
        Assert.Equal(0, failed.Attempts);
        Assert.Null(failed.Failure);
        Assert.Equal([ArcaRobotSteps.PlatformCertificate], failed.CompletedSteps);
        Assert.True(failed.TryReserve(Owner, Now.AddMinutes(1), Lease));

        var done = ArcaRobotRun.SchedulePlatform(ConnectionId, Now);
        done.TryReserve(Owner, Now, Lease);
        done.Succeed(Owner, Now);
        done.Reschedule(Now.AddMinutes(1));
        Assert.Equal(ArcaRobotRunStatus.Succeeded, done.Status);
    }

    [Fact]
    public void An_alert_is_sent_once_per_reason_and_step_within_the_interval_even_after_a_restart()
    {
        var run = ArcaRobotRun.StartCompany(ConnectionId, Operator, Owner, Now, Lease);
        const string key = "ScreenChanged:company.delegate.wsfe";

        Assert.True(run.ShouldAlert(key, Now, TimeSpan.FromDays(1)));
        run.Alerted(key, Now);

        Assert.False(run.ShouldAlert(key, Now.AddHours(23), TimeSpan.FromDays(1)));
        Assert.True(run.ShouldAlert("Captcha:company.login", Now.AddHours(1), TimeSpan.FromDays(1)));
        Assert.True(run.ShouldAlert(key, Now.AddDays(1), TimeSpan.FromDays(1)));

        run.Fail(ArcaRobotFailure.ScreenChanged, "company.delegate.wsfe", Owner, Now);
        run.RestartCompany(Operator, Owner, Now.AddMinutes(1), Lease);
        Assert.False(run.ShouldAlert(key, Now.AddHours(2), TimeSpan.FromDays(1)));
    }

    [Fact]
    public void The_step_keys_of_a_service_carry_its_code()
    {
        var wsfe = ArcaServiceCode.Create("wsfe")!.Value;

        Assert.Equal("company.delegate.wsfe", ArcaRobotSteps.CompanyDelegate(wsfe));
        Assert.Equal("platform.authorize.wsfe", ArcaRobotSteps.PlatformAuthorize(wsfe));
        Assert.Equal("platform.assign.wsfe", ArcaRobotSteps.PlatformAssign(wsfe));
    }
}
