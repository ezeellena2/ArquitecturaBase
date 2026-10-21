using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed class WhatsAppFlowDeprecationServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    [Fact]
    public async Task Reservation_uses_the_owner_lock_before_the_flow_and_one_transaction()
    {
        var fixture = new WhatsAppFlowDeprecationFixture();
        var result = await fixture.Service().ReserveAsync(fixture.Binding.Id, Ct);
        Assert.True(result.IsSuccess); Assert.NotNull(result.Value);
        Assert.Equal(["Definitions", "Flow"], fixture.Locks);
        Assert.Equal(1, fixture.UnitOfWork.Transactions);
        Assert.Equal(WhatsAppFlowDeprecationStep.Deprecate, result.Value.Step);
        Assert.Equal(fixture.Operation.VersionId, result.Value.VersionId);
        Assert.Equal(fixture.Manifest.Account, result.Value.Manifest.Account);
        Assert.False(fixture.Binding.DeprecationInFlight);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_owner_used_version_or_pending_session_work_does_not_reserve(bool used)
    {
        var fixture = new WhatsAppFlowDeprecationFixture { Unused = !used, HasOpenWork = !used };
        var result = await fixture.Service().ReserveAsync(fixture.Binding.Id, Ct);
        Assert.True(result.IsSuccess); Assert.Null(result.Value);
        Assert.Equal(0, fixture.Binding.DeprecationFence); Assert.False(fixture.Binding.DeprecationInFlight);
        if (used) Assert.True(fixture.Binding.DeprecationNextCheckAtUtc > fixture.Now);
        else Assert.Null(fixture.Binding.DeprecationNextCheckAtUtc);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Mark_revalidates_the_version_and_pending_work_before_claiming_the_post(bool used)
    {
        var fixture = new WhatsAppFlowDeprecationFixture(); var service = fixture.Service();
        var work = (await service.ReserveAsync(fixture.Binding.Id, Ct)).Value!;
        Assert.NotNull(work);
        fixture.Unused = !used; fixture.HasOpenWork = !used;
        Assert.True((await service.MarkEffectStartedAsync(work, Ct)).IsFailure);
        Assert.False(fixture.Binding.DeprecationInFlight);
    }
    [Fact]
    public async Task A_second_worker_and_stale_completion_cannot_replace_the_fenced_attempt()
    {
        var fixture = new WhatsAppFlowDeprecationFixture(); var service = fixture.Service();
        var first = (await service.ReserveAsync(fixture.Binding.Id, Ct)).Value!; Assert.NotNull(first);
        Assert.Null((await service.ReserveAsync(fixture.Binding.Id, Ct)).Value);
        Assert.True((await service.MarkEffectStartedAsync(first, Ct)).IsSuccess);
        fixture.Clock.Advance(TimeSpan.FromMinutes(3));
        var second = (await service.ReserveAsync(fixture.Binding.Id, Ct)).Value!; Assert.NotNull(second);
        Assert.Equal(WhatsAppFlowDeprecationStep.Verify, second.Step);
        Assert.True(second.Fence > first.Fence);
        Assert.True((await service.CompleteEffectAsync(first, new(true, false, null, []), Ct)).IsFailure);
        Assert.Null(fixture.Binding.DeprecatedAtUtc);
    }
    [Theory]
    [InlineData(WhatsAppFlowRemoteState.Published)]
    [InlineData(WhatsAppFlowRemoteState.Deprecated)]
    [InlineData(WhatsAppFlowRemoteState.Unknown)]
    public async Task Recovery_observes_remote_state_before_another_post(WhatsAppFlowRemoteState remote)
    {
        var fixture = new WhatsAppFlowDeprecationFixture(); var service = fixture.Service();
        var first = (await service.ReserveAsync(fixture.Binding.Id, Ct)).Value!; Assert.NotNull(first);
        Assert.True((await service.MarkEffectStartedAsync(first, Ct)).IsSuccess);
        Assert.True((await service.CompleteEffectAsync(first, new(false, true, null, []), Ct)).IsSuccess);
        fixture.Clock.Advance(TimeSpan.FromMinutes(6));
        var verify = (await service.ReserveAsync(fixture.Binding.Id, Ct)).Value!; Assert.NotNull(verify);
        Assert.Equal(WhatsAppFlowDeprecationStep.Verify, verify.Step);
        Assert.True((await service.CompleteVerificationAsync(verify, new(true, remote), Ct)).IsSuccess);
        var expected = remote == WhatsAppFlowRemoteState.Deprecated ? WhatsAppFlowDeprecationState.Deprecated
            : remote == WhatsAppFlowRemoteState.Published ? WhatsAppFlowDeprecationState.Pending : WhatsAppFlowDeprecationState.Uncertain;
        Assert.Equal(expected, fixture.Binding.DeprecationState);
        Assert.Equal(remote == WhatsAppFlowRemoteState.Deprecated, fixture.Binding.DeprecatedAtUtc.HasValue);
    }
    [Fact]
    public async Task A_pre_request_account_failure_retains_prior_uncertainty()
    {
        var fixture = new WhatsAppFlowDeprecationFixture(); var service = fixture.Service();
        var work = (await service.ReserveAsync(fixture.Binding.Id, Ct)).Value!; Assert.NotNull(work);
        await service.MarkEffectStartedAsync(work, Ct);
        await service.CompleteEffectAsync(work, new(false, true, null, []), Ct);
        fixture.Clock.Advance(TimeSpan.FromMinutes(6));
        var verify = (await service.ReserveAsync(fixture.Binding.Id, Ct)).Value!; Assert.NotNull(verify);
        Assert.True((await service.CompleteEffectAsync(verify, new(false, false, null, [], FlowErrors.AccountUnavailable.Code), Ct)).IsSuccess);
        Assert.Equal(WhatsAppFlowDeprecationState.Uncertain, fixture.Binding.DeprecationState);
    }
}
