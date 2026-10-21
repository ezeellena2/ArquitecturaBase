using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Domain.UnitTests.Modules.WhatsApp;

public sealed class WhatsAppFlowDeprecationTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    [Fact]
    public void Only_a_published_flow_can_reserve_deprecation_and_a_live_lease_cannot_be_replaced()
    {
        var unpublished = WhatsAppFlowBinding.Create(Guid.NewGuid(), Guid.NewGuid(), "es", "unpublished", [1]);
        Assert.True(unpublished.ReserveDeprecation(Now, Lease).IsFailure);
        var binding = Published();
        var reserved = binding.ReserveDeprecation(Now, Lease);
        Assert.True(reserved.IsSuccess);
        Assert.Equal(1, reserved.Value);
        Assert.True(binding.OwnsDeprecationLease(reserved.Value, Now));
        Assert.False(binding.DeprecationInFlight);
        Assert.True(binding.ReserveDeprecation(Now.AddMinutes(1), Lease).IsFailure);
    }

    [Fact]
    public void Reservation_expiry_without_an_effect_remains_safe_to_attempt()
    {
        var binding = Published();
        var first = binding.ReserveDeprecation(Now, Lease);
        Assert.True(first.IsSuccess);
        var second = binding.ReserveDeprecation(Now.AddMinutes(3), Lease);
        Assert.True(second.IsSuccess);
        Assert.True(second.Value > first.Value);
        Assert.Equal(WhatsAppFlowDeprecationState.Pending, binding.DeprecationState);
        Assert.False(binding.DeprecationInFlight);
        Assert.True(binding.MarkDeprecationStarted(first.Value, Now.AddMinutes(3)).IsFailure);
    }

    [Fact]
    public void A_started_effect_survives_lease_expiry_as_uncertain_and_rejects_stale_completion()
    {
        var binding = Published();
        var first = binding.ReserveDeprecation(Now, Lease);
        Assert.True(first.IsSuccess);
        Assert.True(binding.MarkDeprecationStarted(first.Value, Now).IsSuccess);
        var second = binding.ReserveDeprecation(Now.AddMinutes(3), Lease);
        Assert.True(second.IsSuccess);
        Assert.Equal(WhatsAppFlowDeprecationState.Uncertain, binding.DeprecationState);
        Assert.True(binding.MarkDeprecationStarted(second.Value, Now.AddMinutes(3)).IsFailure);
        Assert.True(binding.RecordDeprecationEffect(first.Value, Now.AddMinutes(3), true, false).IsFailure);
        Assert.Null(binding.DeprecatedAtUtc);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    public void Unknown_or_mismatched_verification_never_authorizes_another_post(bool identityMatches, bool published, bool deprecated)
    {
        var binding = Uncertain();
        var next = binding.ReserveDeprecation(Now.AddMinutes(6), Lease);
        Assert.True(next.IsSuccess);
        Assert.True(binding.RecordDeprecationVerification(next.Value, Now.AddMinutes(6), identityMatches, published, deprecated).IsSuccess);
        Assert.Equal(WhatsAppFlowDeprecationState.Uncertain, binding.DeprecationState);
        Assert.True(binding.MarkDeprecationStarted(next.Value, Now.AddMinutes(6)).IsFailure);
        Assert.Null(binding.DeprecatedAtUtc);
    }

    [Fact]
    public void A_confirmed_published_state_allows_only_a_later_fenced_attempt()
    {
        var binding = Uncertain();
        var verification = binding.ReserveDeprecation(Now.AddMinutes(6), Lease);
        Assert.True(verification.IsSuccess);
        Assert.True(binding.RecordDeprecationVerification(verification.Value, Now.AddMinutes(6), true, true, false).IsSuccess);
        Assert.Equal(WhatsAppFlowDeprecationState.Pending, binding.DeprecationState);
        Assert.False(binding.DeprecationInFlight);
        Assert.True(binding.MarkDeprecationStarted(verification.Value, Now.AddMinutes(6)).IsFailure);
        var next = binding.ReserveDeprecation(Now.AddMinutes(7), Lease);
        Assert.True(next.IsSuccess);
        Assert.True(next.Value > verification.Value);
        Assert.True(binding.MarkDeprecationStarted(next.Value, Now.AddMinutes(7)).IsSuccess);
    }

    [Fact]
    public void Confirmed_deprecation_is_terminal_and_preserves_the_original_publication()
    {
        var binding = Published();
        var originalFence = binding.Fence; var originalVerifiedAtUtc = binding.VerifiedAtUtc;
        var originalBytes = binding.ProtectedProjection.ToArray();
        var fence = binding.ReserveDeprecation(Now, Lease);
        Assert.True(fence.IsSuccess);
        Assert.True(binding.MarkDeprecationStarted(fence.Value, Now).IsSuccess);
        Assert.True(binding.RecordDeprecationEffect(fence.Value, Now, true, false).IsSuccess);
        Assert.Equal(WhatsAppFlowDeprecationState.Deprecated, binding.DeprecationState);
        Assert.Equal(Now, binding.DeprecatedAtUtc);
        Assert.Null(binding.DeprecationLeaseExpiresAtUtc);
        Assert.True(binding.ReserveDeprecation(Now.AddDays(1), Lease).IsFailure);
        Assert.True(binding.RecordDeprecationEffect(fence.Value, Now, false, true).IsFailure);
        Assert.Equal(WhatsAppFlowPublicationState.Published, binding.State);
        Assert.Equal(WhatsAppFlowStep.Verify, binding.Step);
        Assert.Equal(originalFence, binding.Fence);
        Assert.Equal(originalVerifiedAtUtc, binding.VerifiedAtUtc);
        Assert.Equal(originalBytes, binding.ProtectedProjection);
        Assert.Equal("123", binding.MetaFlowId);
    }

    [Fact]
    public void A_deprecated_state_observed_after_a_lost_response_confirms_without_a_second_post()
    {
        var binding = Uncertain();
        var fence = binding.ReserveDeprecation(Now.AddMinutes(6), Lease);
        Assert.True(fence.IsSuccess);
        Assert.True(binding.RecordDeprecationVerification(fence.Value, Now.AddMinutes(6), true, false, true).IsSuccess);
        Assert.Equal(WhatsAppFlowDeprecationState.Deprecated, binding.DeprecationState);
        Assert.Equal(Now.AddMinutes(6), binding.DeprecatedAtUtc);
        Assert.True(binding.MarkDeprecationStarted(fence.Value, Now.AddMinutes(6)).IsFailure);
    }

    [Fact]
    public void A_known_pre_request_failure_does_not_erase_prior_uncertainty()
    {
        var binding = Uncertain();
        var fence = binding.ReserveDeprecation(Now.AddMinutes(6), Lease);
        Assert.True(fence.IsSuccess);
        Assert.True(binding.RecordDeprecationEffect(fence.Value, Now.AddMinutes(6), false, false).IsSuccess);
        Assert.Equal(WhatsAppFlowDeprecationState.Uncertain, binding.DeprecationState);
        Assert.Null(binding.DeprecatedAtUtc);
    }

    [Fact]
    public void Unused_checks_can_be_deferred_without_claiming_an_effect_or_changing_publication()
    {
        var binding = Published();
        binding.DeferDeprecationCheck(Now);
        Assert.True(binding.DeprecationNextCheckAtUtc > Now);
        Assert.False(binding.DeprecationInFlight);
        Assert.Equal(0, binding.DeprecationFence);
        Assert.True(binding.ReserveDeprecation(Now, Lease).IsFailure);
        Assert.True(binding.ReserveDeprecation(Now.AddMinutes(6), Lease).IsSuccess);
    }

    private static WhatsAppFlowBinding Uncertain()
    {
        var binding = Published();
        var fence = binding.ReserveDeprecation(Now, Lease);
        Assert.True(fence.IsSuccess);
        Assert.True(binding.MarkDeprecationStarted(fence.Value, Now).IsSuccess);
        Assert.True(binding.RecordDeprecationEffect(fence.Value, Now, false, true).IsSuccess);
        return binding;
    }

    private static WhatsAppFlowBinding Published()
    {
        var binding = WhatsAppFlowBinding.Create(Guid.NewGuid(), Guid.NewGuid(), "es", "published", [1, 2, 3]);
        binding.Begin(1, Now); binding.MarkEffectStarted(1); binding.RecordEffect(1, Now, "123", true, false, false);
        binding.Begin(2, Now); binding.MarkEffectStarted(2); binding.RecordEffect(2, Now, null, true, false, false);
        binding.Begin(3, Now); binding.RecordVerification(3, Now, false, true, true, true, true);
        binding.Begin(4, Now); binding.MarkEffectStarted(4); binding.RecordEffect(4, Now, null, true, false, false);
        binding.Begin(5, Now); binding.RecordVerification(5, Now, true, false, true, true, true);
        Assert.Equal(WhatsAppFlowPublicationState.Published, binding.State);
        return binding;
    }
}
