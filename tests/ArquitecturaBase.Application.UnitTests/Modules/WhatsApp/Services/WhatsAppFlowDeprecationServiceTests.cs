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

    [Theory]
    [InlineData(WhatsAppFlowRemoteState.Published)]
    [InlineData(WhatsAppFlowRemoteState.Deprecated)]
    [InlineData(WhatsAppFlowRemoteState.Unknown)]
    public async Task A_rejected_post_is_persisted_and_observed_before_retry(WhatsAppFlowRemoteState remote)
    {
        var fixture = new WhatsAppFlowDeprecationFixture();
        var first = (await fixture.Service().ReserveAsync(fixture.Binding.Id, Ct)).Value!;
        Assert.True((await fixture.Service().MarkEffectStartedAsync(first, Ct)).IsSuccess);
        Assert.True((await fixture.Service().CompleteEffectAsync(first, new(false, false, null, [], "WhatsApp.Flow.DeprecationRejected"), Ct)).IsSuccess);
        Assert.Equal(WhatsAppFlowDeprecationState.Uncertain, fixture.Binding.DeprecationState);
        Assert.True(fixture.Binding.DeprecationInFlight);
        AdvanceToNextCheck(fixture);
        var verify = (await fixture.Service().ReserveAsync(fixture.Binding.Id, Ct)).Value!;
        Assert.NotNull(verify);
        Assert.Equal(WhatsAppFlowDeprecationStep.Verify, verify.Step);
        Assert.True((await fixture.Service().MarkEffectStartedAsync(verify, Ct)).IsFailure);
        Assert.True((await fixture.Service().CompleteVerificationAsync(verify, new(true, remote), Ct)).IsSuccess);
        Assert.Equal(remote == WhatsAppFlowRemoteState.Deprecated ? "Deprecated" : "NeedsAttention", fixture.Binding.DeprecationState.ToString());
        Assert.Equal(remote == WhatsAppFlowRemoteState.Deprecated, fixture.Binding.DeprecatedAtUtc.HasValue);
        if (remote == WhatsAppFlowRemoteState.Deprecated) return;
        Assert.True(fixture.Binding.DeprecationNextCheckAtUtc >= fixture.Now.AddMinutes(5));
        Assert.Null((await fixture.Service().ReserveAsync(fixture.Binding.Id, Ct)).Value);
        AdvanceToNextCheck(fixture);
        var later = (await fixture.Service().ReserveAsync(fixture.Binding.Id, Ct)).Value!;
        Assert.NotNull(later);
        Assert.Equal(remote == WhatsAppFlowRemoteState.Published ? WhatsAppFlowDeprecationStep.Deprecate : WhatsAppFlowDeprecationStep.Verify, later.Step);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Account_failures_are_visible_and_have_a_growing_durable_delay(bool unavailable)
    {
        var fixture = new WhatsAppFlowDeprecationFixture();
        foreach (var minutes in new[] { 5, 15, 30, 60, 60 })
        {
            var work = (await fixture.Service().ReserveAsync(fixture.Binding.Id, Ct)).Value!;
            Assert.NotNull(work);
            var code = unavailable ? FlowErrors.AccountUnavailable.Code : FlowErrors.AccountMismatch.Code;
            Assert.True((await fixture.Service().CompleteEffectAsync(work, new(false, false, null, [], code), Ct)).IsSuccess);
            Assert.Equal("NeedsAttention", fixture.Binding.DeprecationState.ToString());
            Assert.Equal(fixture.Now.AddMinutes(minutes), fixture.Binding.DeprecationNextCheckAtUtc);
            Assert.False(fixture.Binding.DeprecationInFlight);
            Assert.Null((await fixture.Service().ReserveAsync(fixture.Binding.Id, Ct)).Value);
            AdvanceToNextCheck(fixture);
        }
    }

    [Fact]
    public async Task An_unreadable_manifest_is_visible_and_preserves_a_previously_unknown_post()
    {
        var fixture = new WhatsAppFlowDeprecationFixture { UnreadableManifest = true };
        Assert.Null((await fixture.Service().ReserveAsync(fixture.Binding.Id, Ct)).Value);
        Assert.Equal("NeedsAttention", fixture.Binding.DeprecationState.ToString());
        Assert.Equal(fixture.Now.AddMinutes(5), fixture.Binding.DeprecationNextCheckAtUtc);
        fixture.UnreadableManifest = false;
        AdvanceToNextCheck(fixture);
        var work = (await fixture.Service().ReserveAsync(fixture.Binding.Id, Ct)).Value!;
        Assert.NotNull(work);
        Assert.True((await fixture.Service().MarkEffectStartedAsync(work, Ct)).IsSuccess);
        Assert.True((await fixture.Service().CompleteEffectAsync(work, new(false, true, null, []), Ct)).IsSuccess);
        fixture.UnreadableManifest = true;
        AdvanceToNextCheck(fixture);
        Assert.Null((await fixture.Service().ReserveAsync(fixture.Binding.Id, Ct)).Value);
        Assert.Equal("NeedsAttention", fixture.Binding.DeprecationState.ToString());
        Assert.True(fixture.Binding.DeprecationInFlight);
        fixture.UnreadableManifest = false;
        AdvanceToNextCheck(fixture);
        var verify = (await fixture.Service().ReserveAsync(fixture.Binding.Id, Ct)).Value!;
        Assert.NotNull(verify);
        Assert.Equal(WhatsAppFlowDeprecationStep.Verify, verify.Step);
    }

    private static void AdvanceToNextCheck(WhatsAppFlowDeprecationFixture fixture) =>
        fixture.Clock.SetUtcNow(fixture.Binding.DeprecationNextCheckAtUtc!.Value.AddSeconds(1));

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
        Assert.Equal("NeedsAttention", fixture.Binding.DeprecationState.ToString());
        Assert.True(fixture.Binding.DeprecationInFlight);
        AdvanceToNextCheck(fixture);
        Assert.Equal(WhatsAppFlowDeprecationStep.Verify, (await fixture.Service().ReserveAsync(fixture.Binding.Id, Ct)).Value!.Step);
    }
}
