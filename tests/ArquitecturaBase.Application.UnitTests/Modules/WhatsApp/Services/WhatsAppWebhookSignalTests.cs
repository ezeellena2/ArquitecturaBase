using ArquitecturaBase.Application.Common.Exceptions;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed class WhatsAppWebhookSignalTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTime Now = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task A_flow_reply_wakes_both_workers_only_after_commit()
    {
        var fixture = new WhatsAppWebhookSignalFixture { Batch = Batch(WhatsAppMessageKind.FlowReply) };

        Assert.True(await fixture.ReceiveAsync(Ct));

        Assert.Equal(["commit", "inbound", "flow"], fixture.Events);
        Assert.Equal(1, fixture.SuccessfulCommits);
        Assert.Equal(CommitPolicy.OnSuccess, fixture.UnitOfWork.LastPolicy);
        Assert.Equal(0, fixture.RetryCalls);
    }

    [Fact]
    public async Task A_text_message_wakes_only_the_inbound_worker_after_commit()
    {
        var fixture = new WhatsAppWebhookSignalFixture { Batch = Batch(WhatsAppMessageKind.Text) };

        Assert.True(await fixture.ReceiveAsync(Ct));

        Assert.Equal(["commit", "inbound"], fixture.Events);
        Assert.Equal(1, fixture.SuccessfulCommits);
        Assert.Equal(0, fixture.RetryCalls);
    }

    [Fact]
    public async Task A_flow_reply_retried_after_a_unique_conflict_wakes_workers_after_the_fresh_commit()
    {
        var fixture = new WhatsAppWebhookSignalFixture { Batch = Batch(WhatsAppMessageKind.FlowReply) };
        fixture.UnitOfWork.CommitFailure = new UniqueConstraintViolationException();

        Assert.True(await fixture.ReceiveAsync(Ct));

        Assert.Equal(["commit", "inbound", "flow"], fixture.Events);
        Assert.Equal(1, fixture.UnitOfWork.Rollbacks);
        Assert.Equal(1, fixture.RetryCalls);
        Assert.Equal(1, fixture.RetryUnitOfWork.Transactions);
        Assert.Equal(1, fixture.RetryUnitOfWork.Commits);
        Assert.Equal(CommitPolicy.OnSuccess, fixture.RetryUnitOfWork.LastPolicy);
        Assert.Equal(1, fixture.SuccessfulCommits);
    }

    [Fact]
    public async Task An_invalid_signature_never_reads_persists_or_wakes_a_worker()
    {
        var fixture = new WhatsAppWebhookSignalFixture
        { Batch = Batch(WhatsAppMessageKind.FlowReply), SignatureValid = false };

        Assert.False(await fixture.ReceiveAsync(Ct));

        Assert.Empty(fixture.Events);
        Assert.Equal(0, fixture.ReadCalls);
        Assert.Equal(0, fixture.PersistCalls);
        Assert.Equal(0, fixture.RetryCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_failed_primary_or_retry_commit_never_wakes_a_worker(bool retry)
    {
        var fixture = new WhatsAppWebhookSignalFixture { Batch = Batch(WhatsAppMessageKind.FlowReply) };
        var failure = new InvalidOperationException("Simulated commit failure.");
        fixture.UnitOfWork.CommitFailure = retry ? new UniqueConstraintViolationException() : failure;
        if (retry) fixture.RetryUnitOfWork.CommitFailure = failure;

        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.ReceiveAsync(Ct)));

        Assert.Empty(fixture.Events);
        Assert.Equal(0, fixture.SuccessfulCommits);
        Assert.Equal(1, fixture.UnitOfWork.Rollbacks);
        Assert.Equal(retry ? 1 : 0, fixture.RetryCalls);
        Assert.Equal(retry ? 1 : 0, fixture.RetryUnitOfWork.Rollbacks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_webhook_without_messages_never_wakes_a_worker(bool hasStatus)
    {
        var fixture = new WhatsAppWebhookSignalFixture
        {
            Batch = hasStatus ? new([], [new WhatsAppWebhookStatus("wamid.status", WhatsAppMessageStatus.Delivered, Now, null)])
                : WhatsAppWebhookBatch.Empty,
        };

        Assert.True(await fixture.ReceiveAsync(Ct));

        Assert.Equal(hasStatus ? ["commit"] : [], fixture.Events);
        Assert.Equal(hasStatus ? 1 : 0, fixture.SuccessfulCommits);
        Assert.Equal(0, fixture.RetryCalls);
    }

    private static WhatsAppWebhookBatch Batch(WhatsAppMessageKind kind) => new(
        [new WhatsAppWebhookMessage("wamid.signal-test", new WhatsAppWebhookContact("5493415550000", null, "Ana"),
            kind, kind == WhatsAppMessageKind.Text ? "Hola" : null, null, Now,
            kind == WhatsAppMessageKind.FlowReply ? "{}" : null)], []);
}
