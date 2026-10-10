using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Domain.UnitTests.Modules.WhatsApp;

public sealed class WhatsAppFlowBindingTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
    [Fact]
    public void Create_without_a_confirmed_id_never_repeats_create_after_reservation_or_restart()
    {
        var binding = NewBinding();
        Assert.Equal(WhatsAppFlowStep.Create, binding.Begin(1, Now).Value);
        Assert.True(binding.RecordEffect(1, Now, null, false, false, true).IsSuccess);
        Assert.Equal(WhatsAppFlowPublicationState.Uncertain, binding.State);
        Assert.Equal(WhatsAppFlowStep.Verify, binding.Begin(2, Now.AddMinutes(2)).Value);
        Assert.Equal(WhatsAppFlowStep.Create, binding.Step);
        Assert.Null(binding.MetaFlowId);
        Assert.True(binding.RecordEffect(1, Now, "123", true, false, false).IsFailure);
        Assert.Null(binding.MetaFlowId);
    }
    [Fact]
    public void Upload_success_with_validation_errors_cannot_advance_to_publish()
    {
        var binding = NewBinding();
        binding.Begin(1, Now); binding.RecordEffect(1, Now, "123", true, false, false);
        Assert.Equal(WhatsAppFlowStep.Upload, binding.Begin(2, Now).Value);
        Assert.True(binding.RecordEffect(2, Now, null, true, true, false).IsSuccess);
        Assert.Equal(WhatsAppFlowPublicationState.Failed, binding.State);
        Assert.True(binding.Begin(3, Now).IsFailure);
    }
    [Fact]
    public void Published_requires_verified_asset_owner_and_health_and_rejects_late_updates()
    {
        var binding = NewBinding(); binding.Begin(1, Now); binding.RecordEffect(1, Now, "123", true, false, false);
        binding.Begin(2, Now); binding.RecordEffect(2, Now, null, true, false, false);
        Assert.Equal(WhatsAppFlowStep.Validate, binding.Begin(3, Now).Value);
        Assert.True(binding.RecordVerification(3, Now, false, true, true, true, true).IsSuccess);
        Assert.Equal(WhatsAppFlowStep.Publish, binding.Begin(4, Now).Value);
        binding.RecordEffect(4, Now, null, true, false, false);
        Assert.Equal(WhatsAppFlowPublicationState.Publishing, binding.State);
        binding.Begin(5, Now);
        Assert.True(binding.RecordVerification(5, Now, true, false, false, true, true).IsSuccess);
        Assert.NotEqual(WhatsAppFlowPublicationState.Published, binding.State);
        binding.Begin(6, Now);
        Assert.True(binding.RecordVerification(6, Now, true, false, true, true, true).IsSuccess);
        Assert.Equal(WhatsAppFlowPublicationState.Published, binding.State);
        Assert.True(binding.RecordEffect(5, Now, null, false, false, true).IsFailure);
        Assert.Equal(WhatsAppFlowPublicationState.Published, binding.State);
    }
    [Fact]
    public void Rejected_creation_preserves_the_confirmed_external_identity()
    {
        var binding = NewBinding(); binding.Begin(1, Now);
        Assert.True(binding.RecordEffect(1, Now, "123", true, true, false).IsSuccess);
        Assert.Equal("123", binding.MetaFlowId);
        Assert.Equal(WhatsAppFlowPublicationState.Failed, binding.State);
    }
    [Fact]
    public void Verification_without_a_known_identity_cannot_mark_a_flow_published()
    {
        var binding = NewBinding(); binding.Begin(1, Now);
        Assert.True(binding.RecordVerification(1, Now, true, false, true, true, true).IsSuccess);
        Assert.Equal(WhatsAppFlowPublicationState.Uncertain, binding.State);
    }
    [Fact]
    public void A_transient_account_failure_retries_the_same_step_without_losing_a_known_flow_id()
    {
        var fresh = NewBinding();
        fresh.Begin(1, Now);
        Assert.True(fresh.RetryWithoutEffect(1).IsSuccess);
        Assert.Equal(WhatsAppFlowPublicationState.Accepted, fresh.State);
        Assert.False(fresh.InFlight);
        Assert.Equal(WhatsAppFlowStep.Create, fresh.Begin(2, Now.AddMinutes(2)).Value);

        var existing = NewBinding(); existing.Begin(1, Now); existing.RecordEffect(1, Now, "123", true, false, false);
        existing.Begin(2, Now);
        Assert.True(existing.RetryWithoutEffect(2).IsSuccess);
        Assert.Equal("123", existing.MetaFlowId);
        Assert.Equal(WhatsAppFlowPublicationState.Publishing, existing.State);
        Assert.False(existing.InFlight);
        Assert.Equal(WhatsAppFlowStep.Upload, existing.Begin(3, Now.AddMinutes(2)).Value);
        Assert.True(existing.PreserveUncertainWithoutEffect(3).IsSuccess);
        Assert.Equal("123", existing.MetaFlowId);
        Assert.Equal(WhatsAppFlowPublicationState.Uncertain, existing.State);
        Assert.Equal(WhatsAppFlowStep.Verify, existing.Begin(4, Now.AddMinutes(4)).Value);
    }
    private static WhatsAppFlowBinding NewBinding() => WhatsAppFlowBinding.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "es", "operation_es", new byte[] { 1, 2 });
}
