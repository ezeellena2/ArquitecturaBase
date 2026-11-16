using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Integrations.Emails;
using ArquitecturaBase.Application.Models.Emails;
using Microsoft.Extensions.DependencyInjection;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.Delivery;
using ArquitecturaBase.Infrastructure.Delivery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Api.IntegrationTests.Emails;

public sealed class TransactionalEmailQueueTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly string[] Types = ["test"];

    // N27 and H25: the queue gives a copy only the message types its version reads.
    [Fact]
    public async Task A_delivery_of_a_type_this_version_does_not_know_is_not_claimed()
    {
        var channel = "queue-test-" + Guid.NewGuid().ToString("N");
        var ct = TestContext.Current.CancellationToken;
        await factory.ExecuteScopeAsync(provider => provider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(_ =>
        {
            Assert.True(provider.GetRequiredService<DurableMessageStore>().TryEnqueue(channel, 2, "test-v2", "newer-content", null));
            return Task.FromResult(Result.Success());
        }, CommitPolicy.OnSuccess, ct));

        var claimed = await factory.ExecuteScopeAsync(provider => provider.GetRequiredService<IDeliveryQueueService>().ClaimNextAsync(channel, Types, ct));

        Assert.Null(claimed.Value);
        Assert.True(await factory.ExecuteDbContextAsync(db => db.Set<QueuedDelivery>().AnyAsync(delivery => delivery.Channel == channel && delivery.ClaimedAtUtc == null, ct)));
    }

    // N27 and H25: what a copy could not read goes back with a wait, for another copy, and only at its cap is it deleted.
    [Fact]
    public async Task A_delivery_a_copy_could_not_read_waits_for_another_up_to_its_cap()
    {
        var channel = "queue-test-" + Guid.NewGuid().ToString("N");
        var ct = TestContext.Current.CancellationToken;
        await factory.ExecuteScopeAsync(provider => provider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(_ =>
        {
            Assert.True(provider.GetRequiredService<DurableMessageStore>().TryEnqueue(channel, 60, "test", "unread-content", null));
            return Task.FromResult(Result.Success());
        }, CommitPolicy.OnSuccess, ct));
        Task<Result<ArquitecturaBase.Application.Models.Delivery.DeliveryMessageResponse?>> ClaimAsync() =>
            factory.ExecuteScopeAsync(provider => provider.GetRequiredService<IDeliveryQueueService>().ClaimNextAsync(channel, Types, ct));

        var first = (await ClaimAsync()).Value!;
        await factory.ExecuteScopeAsync(provider => provider.GetRequiredService<IDeliveryQueueService>().LeaveForAnotherCopyAsync(first.Id, ct));

        var left = await factory.ExecuteDbContextAsync(db => db.Set<QueuedDelivery>().SingleAsync(delivery => delivery.Id == first.Id, ct));
        Assert.Equal(((DateTime?)null, 1), (left.ClaimedAtUtc, left.ReadFailures));
        Assert.Null((await ClaimAsync()).Value);
        for (var read = 1; read < 10; read++)
        {
            factory.Clock.Advance(TimeSpan.FromSeconds(31));
            var again = (await ClaimAsync()).Value!;
            Assert.Equal(first.Id, again.Id);
            await factory.ExecuteScopeAsync(provider => provider.GetRequiredService<IDeliveryQueueService>().LeaveForAnotherCopyAsync(again.Id, ct));
        }
        factory.Clock.Advance(TimeSpan.FromSeconds(31));
        var last = (await ClaimAsync()).Value!;
        await factory.ExecuteScopeAsync(provider => provider.GetRequiredService<IDeliveryQueueService>().LeaveForAnotherCopyAsync(last.Id, ct));

        Assert.False(await factory.ExecuteDbContextAsync(db => db.Set<QueuedDelivery>().AnyAsync(delivery => delivery.Channel == channel, ct)));
    }

    [Fact]
    public async Task Enqueue_requires_the_callers_transaction()
    {
        await factory.ExecuteScopeAsync(provider =>
        {
            var queue = provider.GetRequiredService<IEmailQueue>();
            Assert.Throws<InvalidOperationException>(() => queue.TryEnqueue(new EmailMessage("test@example.test", "subject", "body", "body")));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Rollback_removes_the_delivery_and_a_new_consumer_can_claim_committed_content_once()
    {
        var channel = "queue-test-" + Guid.NewGuid().ToString("N");
        var ct = TestContext.Current.CancellationToken;
        await factory.ExecuteScopeAsync(provider => provider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(_ =>
        {
            Assert.True(provider.GetRequiredService<DurableMessageStore>().TryEnqueue(channel, 2, "test", "private-code-123456", null));
            return Task.FromResult(Result.Failure(UserErrors.NotFound));
        }, CommitPolicy.OnSuccess, ct));
        Assert.False(await factory.ExecuteDbContextAsync(db => db.Set<QueuedDelivery>().AnyAsync(delivery => delivery.Channel == channel, ct)));
        await factory.ExecuteScopeAsync(provider => provider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(_ =>
        {
            Assert.True(provider.GetRequiredService<DurableMessageStore>().TryEnqueue(channel, 2, "test", "private-code-123456", null));
            return Task.FromResult(Result.Success());
        }, CommitPolicy.OnSuccess, ct));
        var persisted = await factory.ExecuteDbContextAsync(db => db.Set<QueuedDelivery>().SingleAsync(delivery => delivery.Channel == channel, ct));
        Assert.DoesNotContain("123456", persisted.ProtectedPayload, StringComparison.Ordinal);
        // Each call resolves a new scope and consumer. A claimed delivery is never selected after a restart.
        var claimed = await factory.ExecuteScopeAsync(provider => provider.GetRequiredService<IDeliveryQueueService>().ClaimNextAsync(channel, Types, ct));
        Assert.Equal(persisted.Id, claimed.Value!.Id);
        var afterRestart = await factory.ExecuteScopeAsync(provider => provider.GetRequiredService<IDeliveryQueueService>().ClaimNextAsync(channel, Types, ct));
        Assert.Null(afterRestart.Value);
        await factory.ExecuteScopeAsync(provider => provider.GetRequiredService<IDeliveryQueueService>().CompleteAsync(persisted.Id, ct));
        Assert.False(await factory.ExecuteDbContextAsync(db => db.Set<QueuedDelivery>().AnyAsync(delivery => delivery.Channel == channel, ct)));
    }

    [Fact]
    public async Task Two_hosts_compete_for_one_delivery_and_only_one_claims_it()
    {
        var channel = "two-hosts-" + Guid.NewGuid().ToString("N");
        var ct = TestContext.Current.CancellationToken;
        await factory.ExecuteScopeAsync(provider => provider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(_ =>
        {
            Assert.True(provider.GetRequiredService<DurableMessageStore>().TryEnqueue(channel, 1, "test", "encrypted-content", null));
            return Task.FromResult(Result.Success());
        }, CommitPolicy.OnSuccess, ct));

        await using var secondHost = factory.WithWebHostBuilder(_ => { });
        await using var secondScope = secondHost.Services.CreateAsyncScope();
        var claims = await Task.WhenAll(
            factory.ExecuteScopeAsync(provider => provider.GetRequiredService<IDeliveryQueueService>().ClaimNextAsync(channel, Types, ct)),
            secondScope.ServiceProvider.GetRequiredService<IDeliveryQueueService>().ClaimNextAsync(channel, Types, ct));
        var winner = Assert.Single(claims, result => result.Value is not null).Value!;
        await factory.ExecuteScopeAsync(provider => provider.GetRequiredService<IDeliveryQueueService>().CompleteAsync(winner.Id, ct));
    }

    [Fact]
    public async Task Expired_content_is_removed_even_without_a_consumer_for_its_channel()
    {
        var channel = "disabled-channel-" + Guid.NewGuid().ToString("N");
        var ct = TestContext.Current.CancellationToken;
        await factory.ExecuteScopeAsync(provider => provider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(_ =>
        {
            Assert.True(provider.GetRequiredService<DurableMessageStore>().TryEnqueue(channel, 1, "test", "private-content", factory.Clock.GetUtcNow().UtcDateTime.AddSeconds(1)));
            return Task.FromResult(Result.Success());
        }, CommitPolicy.OnSuccess, ct));
        factory.Clock.Advance(TimeSpan.FromSeconds(2));
        for (var attempt = 0; attempt < 30; attempt++)
        {
            if (!await factory.ExecuteDbContextAsync(db => db.Set<QueuedDelivery>().AnyAsync(delivery => delivery.Channel == channel, ct))) return;
            await Task.Delay(50, ct);
        }
        Assert.Fail("Expired delivery content remained after the shared consumer's cleanup interval.");
    }

    [Fact]
    public async Task Capacity_includes_messages_added_in_the_same_transaction()
    {
        var channel = "capacity-" + Guid.NewGuid().ToString("N");
        await factory.ExecuteScopeAsync(provider => provider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(_ =>
        {
            var queue = provider.GetRequiredService<DurableMessageStore>();
            Assert.True(queue.TryEnqueue(channel, 1, "test", "first", null));
            Assert.False(queue.TryEnqueue(channel, 1, "test", "second", null));
            return Task.FromResult(Result.Failure(UserErrors.NotFound));
        }, CommitPolicy.OnSuccess, TestContext.Current.CancellationToken));
    }

    // H8: a delivery the worker gives back without having called the provider goes back to its channel, also when the worker stops
    // reading right after, and the next consumer claims it; one the worker finishes is completed even while it stops.
    [Fact]
    public async Task A_delivery_given_back_by_the_worker_is_claimed_again_and_one_it_finishes_is_completed()
    {
        var channel = "queue-test-" + Guid.NewGuid().ToString("N");
        var ct = TestContext.Current.CancellationToken;
        await factory.ExecuteScopeAsync(provider => provider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(_ =>
        {
            Assert.True(provider.GetRequiredService<DurableMessageStore>().TryEnqueue(channel, 2, "test", "private-code-123456", null));
            return Task.FromResult(Result.Success());
        }, CommitPolicy.OnSuccess, ct));
        var source = new TextSource(channel, factory.Services);
        await using (var reader = source.ReadAllAsync(ct).GetAsyncEnumerator(ct))
        {
            Assert.True(await reader.MoveNextAsync());
            Assert.Equal("private-code-123456", reader.Current);
            source.GiveBackCurrent();
        }

        var claimed = await factory.ExecuteScopeAsync(provider => provider.GetRequiredService<IDeliveryQueueService>().ClaimNextAsync(channel, Types, ct));
        Assert.NotNull(claimed.Value);
        await factory.ExecuteScopeAsync(provider => provider.GetRequiredService<IDeliveryQueueService>().ReleaseAsync(claimed.Value!.Id, ct));
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await using (var reader = source.ReadAllAsync(stopping.Token).GetAsyncEnumerator(stopping.Token))
        {
            Assert.True(await reader.MoveNextAsync());
            await stopping.CancelAsync();
            Assert.False(await reader.MoveNextAsync());
        }
        Assert.False(await factory.ExecuteDbContextAsync(db => db.Set<QueuedDelivery>().AnyAsync(delivery => delivery.Channel == channel, ct)));
    }

    // N5 and N8: a send that failed before the body goes back to pending with a wait that doubles, for this or another copy, and
    // the worker's source goes on without completing it.
    [Fact]
    public async Task A_delivery_put_back_to_wait_is_not_completed_and_is_claimed_again_after_its_wait()
    {
        var channel = "queue-test-" + Guid.NewGuid().ToString("N");
        var ct = TestContext.Current.CancellationToken;
        await EnqueueAsync(channel, null);
        var source = new TextSource(channel, factory.Services);
        await using (var reader = source.ReadAllAsync(ct).GetAsyncEnumerator(ct))
        {
            Assert.True(await reader.MoveNextAsync());
            Assert.Equal(DeliveryRetry.Waits, await source.RetryCurrentLaterAsync(TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(15), ct));
        }

        var waiting = await factory.ExecuteDbContextAsync(db => db.Set<QueuedDelivery>().SingleAsync(delivery => delivery.Channel == channel, ct));
        Assert.Equal(((DateTime?)null, 0), (waiting.ClaimedAtUtc, waiting.ReadFailures));
        Assert.True(waiting.AvailableAtUtc > factory.Clock.GetUtcNow().UtcDateTime);
        Assert.Null((await ClaimAsync(channel)).Value);
        factory.Clock.Advance(TimeSpan.FromSeconds(3));
        var again = (await ClaimAsync(channel)).Value!;
        Assert.Equal(waiting.Id, again.Id);
        await factory.ExecuteScopeAsync(provider => provider.GetRequiredService<IDeliveryQueueService>().CompleteAsync(again.Id, ct));
    }

    // N5: when putting it back fails (here, its bounded token), the worker is told it did not go back, which is not that it
    // expired; the row stays claimed, as a crash leaves it, and is never sent again.
    [Fact]
    public async Task A_delivery_that_could_not_go_back_to_wait_stays_claimed_and_is_not_reported_as_expired()
    {
        var channel = "queue-test-" + Guid.NewGuid().ToString("N");
        var ct = TestContext.Current.CancellationToken;
        await EnqueueAsync(channel, null);
        var source = new TextSource(channel, factory.Services);
        await using (var reader = source.ReadAllAsync(ct).GetAsyncEnumerator(ct))
        {
            Assert.True(await reader.MoveNextAsync());
            Assert.Equal(DeliveryRetry.NotReturned, await source.RetryCurrentLaterAsync(TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(15), new CancellationToken(canceled: true)));
        }

        var claimed = await factory.ExecuteDbContextAsync(db => db.Set<QueuedDelivery>().SingleAsync(delivery => delivery.Channel == channel, ct));
        Assert.NotNull(claimed.ClaimedAtUtc);
        await factory.ExecuteScopeAsync(provider => provider.GetRequiredService<IDeliveryQueueService>().CompleteAsync(claimed.Id, ct));
    }

    // N5: when its next attempt would come after its expiry, it expired without going out: it is completed, and the worker is told.
    [Fact]
    public async Task A_delivery_whose_next_attempt_would_come_after_its_expiry_is_completed_and_not_retried()
    {
        var channel = "queue-test-" + Guid.NewGuid().ToString("N");
        var ct = TestContext.Current.CancellationToken;
        await EnqueueAsync(channel, factory.Clock.GetUtcNow().UtcDateTime.AddSeconds(1));
        var claimed = (await ClaimAsync(channel)).Value!;

        var retried = await factory.ExecuteScopeAsync(provider => provider.GetRequiredService<IDeliveryQueueService>()
            .RetryLaterAsync(claimed.Id, TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(15), ct));

        Assert.False(retried.Value);
        Assert.False(await factory.ExecuteDbContextAsync(db => db.Set<QueuedDelivery>().AnyAsync(delivery => delivery.Channel == channel, ct)));
    }

    // O3: a full durable queue does not take the email (a code) and leaves its trace with the id of the email, never its address.
    [Fact]
    public async Task A_full_durable_queue_logs_the_id_of_the_email_it_did_not_take()
    {
        var logger = new Microsoft.Extensions.Logging.Testing.FakeLogger<ArquitecturaBase.Infrastructure.Emails.EmailQueue>();
        var second = new EmailMessage("laura@example.test", "123456 es tu código", "<p>123456</p>", "123456");
        await factory.ExecuteScopeAsync(provider => provider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(_ =>
        {
            var queue = new ArquitecturaBase.Infrastructure.Emails.EmailQueue(
                Microsoft.Extensions.Options.Options.Create(new ArquitecturaBase.Infrastructure.Emails.EmailOptions { QueueCapacity = 1 }),
                logger, provider.GetRequiredService<DurableMessageStore>());
            queue.TryEnqueue(new EmailMessage("ana@example.test", "654321 es tu código", "<p>654321</p>", "654321"));
            Assert.False(queue.TryEnqueue(second));
            return Task.FromResult(Result.Failure(UserErrors.NotFound));
        }, CommitPolicy.OnSuccess, TestContext.Current.CancellationToken));

        var record = Assert.Single(logger.Collector.GetSnapshot(), record => record.Message.Contains(second.Id.ToString(), StringComparison.Ordinal));
        Assert.DoesNotContain("laura@example.test", record.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("123456", record.Message, StringComparison.Ordinal);
    }

    private Task<Result> EnqueueAsync(string channel, DateTime? expiresAtUtc) =>
        factory.ExecuteScopeAsync(provider => provider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(_ =>
        {
            Assert.True(provider.GetRequiredService<DurableMessageStore>().TryEnqueue(channel, 2, "test", "private-code-123456", expiresAtUtc));
            return Task.FromResult(Result.Success());
        }, CommitPolicy.OnSuccess, TestContext.Current.CancellationToken));

    private Task<Result<ArquitecturaBase.Application.Models.Delivery.DeliveryMessageResponse?>> ClaimAsync(string channel) =>
        factory.ExecuteScopeAsync(provider => provider.GetRequiredService<IDeliveryQueueService>().ClaimNextAsync(channel, Types, TestContext.Current.CancellationToken));

    private sealed class TextSource(string channel, IServiceProvider services) : DurableMessageSource<string>(
        services.GetRequiredService<IServiceScopeFactory>(), services.GetRequiredService<IDataProtectionProvider>(), NullLogger.Instance)
    {
        protected override string Channel => channel;
        protected override IReadOnlyCollection<string> MessageTypes => Types;
        protected override string Deserialize(string messageType, string payload) => payload;
    }
}
