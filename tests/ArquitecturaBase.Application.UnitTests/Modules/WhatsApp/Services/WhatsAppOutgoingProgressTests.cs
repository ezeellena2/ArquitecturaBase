using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed class WhatsAppOutgoingProgressTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_empty_or_skipped_pending_row_does_not_report_progress(bool skipped)
    {
        var host = await WhatsAppAssistantDispatchFixture.CreateAsync();
        if (skipped) host.Repository.SkipPending = true;
        else host.Outgoing.ReserveAssistant(host.Now);

        var result = await host.Service.ReserveAsync(Ct);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Progressed);
        Assert.Null(result.Value.Operation);
        Assert.Equal(skipped ? WhatsAppOutgoingMessageState.Pending : WhatsAppOutgoingMessageState.Sending, host.Outgoing.State);
        Assert.Equal(1, host.UnitOfWork.Transactions);
    }

    [Fact]
    public async Task A_rejected_pending_send_reports_its_committed_failure_without_fabricating_a_dispatch()
    {
        var host = await WhatsAppAssistantDispatchFixture.CreateAsync();
        host.Context.Contact.ChangeMode(WhatsAppConversationMode.Manual);

        var result = await host.Service.ReserveAsync(Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(WhatsAppOutgoingMessageState.Failed, host.Outgoing.State);
        Assert.Null(result.Value.Operation);
        Assert.True(result.Value.Progressed);
        Assert.Null(host.Outgoing.DispatchReservationId);
        Assert.Equal(1, host.UnitOfWork.Commits);
        Assert.Empty(host.Messages.Added);
    }
}
