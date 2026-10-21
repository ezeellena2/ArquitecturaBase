using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed partial class WhatsAppFlowReplyServiceTests
{
    [Fact]
    public async Task Completed_capture_wakes_inbound_after_the_capture_is_committed()
    {
        var fixture = new Fixture();
        var message = fixture.AddReply("wamid.capture-notice", fixture.ValidAnswers("captured"));

        await fixture.Service.ProcessPendingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(WhatsAppFlowCaptureState.Completed, message.FlowCaptureState);
        Assert.True(fixture.UnitOfWork.Commits > 0);
        Assert.Equal(1, fixture.Signal.Notifications);
    }

    [Fact]
    public async Task Rejected_capture_does_not_offer_a_follow_up_to_inbound()
    {
        var fixture = new Fixture();
        var answers = fixture.ValidAnswers("invalid");
        answers[fixture.Mapping[2].Key] = "not-a-number";
        var message = fixture.AddReply("wamid.rejected-notice", answers);

        await fixture.Service.ProcessPendingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(WhatsAppFlowCaptureState.Rejected, message.FlowCaptureState);
        Assert.Equal(0, fixture.Signal.Notifications);
    }

    [Fact]
    public async Task Failed_capture_commit_does_not_signal_a_follow_up()
    {
        var fixture = new Fixture();
        fixture.AddReply("wamid.failed-commit-notice", fixture.ValidAnswers("captured"));
        fixture.UnitOfWork.CommitFailure = new InvalidOperationException("The capture commit failed.");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.ProcessPendingAsync(TestContext.Current.CancellationToken));

        Assert.True(fixture.UnitOfWork.Rollbacks > 0);
        Assert.Equal(0, fixture.Signal.Notifications);
    }
}
