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
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Emails;

public sealed class TransactionalEmailQueueTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
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
        var claimed = await factory.ExecuteScopeAsync(provider => provider.GetRequiredService<IDeliveryQueueService>().ClaimNextAsync(channel, ct));
        Assert.Equal(persisted.Id, claimed.Value!.Id);
        var afterRestart = await factory.ExecuteScopeAsync(provider => provider.GetRequiredService<IDeliveryQueueService>().ClaimNextAsync(channel, ct));
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
            factory.ExecuteScopeAsync(provider => provider.GetRequiredService<IDeliveryQueueService>().ClaimNextAsync(channel, ct)),
            secondScope.ServiceProvider.GetRequiredService<IDeliveryQueueService>().ClaimNextAsync(channel, ct));
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
}
