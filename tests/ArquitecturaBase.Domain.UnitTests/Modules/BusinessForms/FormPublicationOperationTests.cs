using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;

public sealed class FormPublicationOperationTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_late_fence_cannot_complete_or_change_an_activated_operation()
    {
        var operation = NewOperation();
        var first = operation.Reserve(Now, TimeSpan.FromMinutes(1));
        Assert.True(first.IsSuccess);
        Assert.True(operation.Reserve(Now.AddSeconds(30), TimeSpan.FromMinutes(1)).IsFailure);
        var next = operation.Reserve(Now.AddMinutes(2), TimeSpan.FromMinutes(1));
        Assert.True(next.IsSuccess);
        Assert.NotEqual(first.Value, next.Value);
        Assert.True(operation.RecordChannelResult(first.Value, Now.AddMinutes(2), "Published", "Verify").IsFailure);
        Assert.Equal(FormPublicationState.Preparing, operation.State);
        Assert.True(operation.RecordChannelResult(next.Value, Now.AddMinutes(2), "Published", "Verify").IsSuccess);
        Assert.Equal(FormPublicationState.ActivationPending, operation.State);
        Assert.True(operation.Complete(next.Value, Now.AddMinutes(2)).IsSuccess);
        Assert.True(operation.RecordChannelResult(next.Value, Now.AddMinutes(2), "Failed", "Publish").IsFailure);
        Assert.Equal(FormPublicationState.Published, operation.State);
    }

    [Fact]
    public void An_operation_that_keeps_coming_back_waits_longer_each_time_up_to_a_cap()
    {
        var operation = NewOperation();
        var fence = operation.Reserve(Now, TimeSpan.FromMinutes(1)).Value;
        Assert.True(operation.RecordChannelResult(fence, Now, "Uncertain", "Create").IsSuccess);
        Assert.Equal(Now.AddSeconds(30), operation.NextCheckAtUtc);
        var later = Now.AddHours(1);
        fence = operation.Reserve(later, TimeSpan.FromMinutes(1)).Value;
        Assert.True(operation.RecordChannelResult(fence, later, "Uncertain", "Create").IsSuccess);
        Assert.Equal(later.AddMinutes(15), operation.NextCheckAtUtc);
        var muchLater = Now.AddDays(3);
        fence = operation.Reserve(muchLater, TimeSpan.FromMinutes(1)).Value;
        Assert.True(operation.RecordChannelResult(fence, muchLater, "Uncertain", "Create").IsSuccess);
        Assert.Equal(muchLater.AddMinutes(30), operation.NextCheckAtUtc);
    }

    [Fact]
    public void Discarding_closes_an_unconfirmed_operation_as_failed_and_fences_the_worker_that_held_it()
    {
        var operation = NewOperation();
        var fence = operation.Reserve(Now, TimeSpan.FromMinutes(1)).Value;
        Assert.True(operation.RecordChannelResult(fence, Now, "Uncertain", "Create").IsSuccess);

        Assert.True(operation.Discard(Now.AddMinutes(1)).IsSuccess);

        Assert.Equal(FormPublicationState.Failed, operation.State);
        Assert.False(operation.IsOpen);
        Assert.Equal("Discarded", operation.Stage);
        Assert.Null(operation.NextCheckAtUtc);
        Assert.True(operation.RecordChannelResult(fence + 1, Now.AddMinutes(1), "Published", "Verify").IsFailure);
        Assert.True(operation.Discard(Now.AddMinutes(2)).IsFailure);
    }

    [Fact]
    public void An_operation_that_is_still_moving_cannot_be_discarded()
    {
        var accepted = NewOperation();
        Assert.True(accepted.Discard(Now).IsFailure);
        Assert.Equal(FormPublicationState.Accepted, accepted.State);

        var held = NewOperation();
        var fence = held.Reserve(Now, TimeSpan.FromMinutes(1)).Value;
        Assert.True(held.Discard(Now).IsFailure);
        Assert.True(held.RecordChannelResult(fence, Now, "Publishing", "Create").IsSuccess);
        Assert.True(held.Discard(Now).IsFailure);
        Assert.Equal(FormPublicationState.Publishing, held.State);
    }

    [Fact]
    public void A_published_operation_cannot_be_discarded()
    {
        var operation = NewOperation();
        var fence = operation.Reserve(Now, TimeSpan.FromMinutes(1)).Value;
        Assert.True(operation.RecordChannelResult(fence, Now, "Published", "Verify").IsSuccess);
        Assert.True(operation.Complete(fence, Now).IsSuccess);
        Assert.True(operation.Discard(Now).IsFailure);
        Assert.Equal(FormPublicationState.Published, operation.State);
    }

    [Fact]
    public void Activation_uses_the_base_version_and_preserves_a_newer_draft()
    {
        var definition = BusinessFormDefinition.Create();
        Assert.True(definition.SetDraft(1, "Form", "{}", "FORM", "es", "{}", "{}", 1, true).IsSuccess);
        definition.LockPrefix();
        Assert.True(definition.SetDraft(1, "New draft", "{}", "FORM", "es", "{}", "{}", 2, false).IsSuccess);
        var versionId = Guid.CreateVersion7();
        Assert.True(definition.ActivateVersion(null, versionId).IsSuccess);
        Assert.Equal(versionId, definition.ActiveVersionId);
        Assert.Equal(2, definition.Revision);
        Assert.Equal("New draft", definition.Name);
        Assert.True(definition.ActivateVersion(null, Guid.CreateVersion7()).IsFailure);
        Assert.Equal(versionId, definition.ActiveVersionId);
        Assert.True(definition.SetDraft(2, "New draft", "{}", "OTHER", "es", "{}", "{}", 2, false).IsFailure);
    }

    [Fact]
    public void An_uncertain_operation_can_be_reserved_without_losing_its_receipt_or_snapshot()
    {
        var operation = NewOperation();
        var fence = operation.Reserve(Now, TimeSpan.FromMinutes(1)).Value;
        Assert.True(operation.RecordChannelResult(fence, Now, "Uncertain", "Create").IsSuccess);
        Assert.True(operation.IsOpen);
        Assert.Equal(FormPublicationState.Uncertain, operation.State);
        Assert.Equal(new byte[] { 1, 2, 3 }, operation.ProtectedManifest);
        Assert.True(operation.Reserve(Now.AddMinutes(2), TimeSpan.FromMinutes(1)).IsSuccess);
        Assert.Equal(new byte[] { 1, 2, 3 }, operation.ProtectedManifest);
    }

    private static BusinessFormPublicationOperation NewOperation() => BusinessFormPublicationOperation.Accept(
        Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, 1, null, new byte[] { 1, 2, 3 }, Now);
}
