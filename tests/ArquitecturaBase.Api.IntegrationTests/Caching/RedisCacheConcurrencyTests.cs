using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Infrastructure.Caching;

namespace ArquitecturaBase.Api.IntegrationTests.Caching;

[Collection(ApiTestGroup.Name)]
public sealed class RedisCacheConcurrencyTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly RedisCacheKey Key = new("tests:value");

    [Fact]
    public async Task Concurrent_misses_across_hosts_execute_one_factory()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var prefix = RedisCacheTests.NewPrefix();
        async Task<string?> Read(CancellationToken ct)
        {
            started.TrySetResult();
            await release.Task.WaitAsync(ct);
            return "shared";
        }
        await using var first = await HostAsync(prefix, Read);
        await using var second = await HostAsync(prefix, Read);
        var requests = Enumerable.Range(0, 32).Select(index => RedisCacheTests.ReadAsync(index % 2 == 0 ? first : second)).ToArray();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        }
        finally
        {
            release.TrySetResult();
        }
        Assert.All(await Task.WhenAll(requests), result => Assert.Equal("shared", result));
        Assert.Equal(1, first.Probe.Calls + second.Probe.Calls);
    }

    [Fact]
    public async Task An_invalidated_factory_cannot_republish_an_old_value()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var prefix = RedisCacheTests.NewPrefix();
        await using var first = await HostAsync(prefix, async ct =>
        {
            started.SetResult();
            await release.Task.WaitAsync(ct);
            return "old";
        });
        await using var second = await HostAsync(prefix, _ => Task.FromResult<string?>("new"));
        var oldRequest = RedisCacheTests.ReadAsync(first);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
            await second.Cache.RemoveAsync(Key, Ct);
            Assert.Equal("new", await RedisCacheTests.ReadAsync(second));
        }
        finally
        {
            release.TrySetResult();
        }
        Assert.Equal("old", await oldRequest);
        Assert.Equal("new", await RedisCacheTests.ReadAsync(first));
    }

    [Fact]
    public async Task An_expired_owner_cannot_publish_or_release_its_successors_lease()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var prefix = RedisCacheTests.NewPrefix();
        await using var first = await HostAsync(prefix, async ct =>
        {
            firstStarted.SetResult();
            await firstRelease.Task.WaitAsync(ct);
            return "old";
        });
        await using var second = await HostAsync(prefix, async ct =>
        {
            secondStarted.SetResult();
            await secondRelease.Task.WaitAsync(ct);
            return "new";
        });
        var oldRequest = RedisCacheTests.ReadAsync(first);
        Task<string?>? newRequest = null;
        try
        {
            await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
            var database = first.Connection.GetDatabase();
            await database.KeyExpireAsync(Key.LeaseKey(prefix), TimeSpan.Zero);
            newRequest = RedisCacheTests.ReadAsync(second);
            await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
            var newOwner = await database.StringGetAsync(Key.LeaseKey(prefix));
            firstRelease.TrySetResult();
            Assert.Equal("old", await oldRequest);
            Assert.Equal(newOwner, await database.StringGetAsync(Key.LeaseKey(prefix)));
            Assert.False(await database.KeyExistsAsync(Key.DataKey(prefix)));
        }
        finally
        {
            firstRelease.TrySetResult();
            secondRelease.TrySetResult();
        }
        Assert.NotNull(newRequest);
        Assert.Equal("new", await newRequest);
        Assert.Equal("new", await RedisCacheTests.ReadAsync(first));
    }

    [Fact]
    public async Task Cancelling_a_fill_disposes_its_scope_and_releases_its_lease()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var prefix = RedisCacheTests.NewPrefix();
        await using var first = await HostAsync(prefix, async ct =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return "unreachable";
        });
        await using var second = await HostAsync(prefix, _ => Task.FromResult<string?>("new"));
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var request = RedisCacheTests.ReadAsync(first, cancelled.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        Assert.True(Assert.Single(first.Probe.Readers).Disposed);
        Assert.False(await first.Connection.GetDatabase().KeyExistsAsync(Key.LeaseKey(prefix)));
        Assert.Equal("new", await RedisCacheTests.ReadAsync(second));
    }

    [Fact]
    public async Task Waiting_for_a_dead_owner_has_a_finite_timeout()
    {
        var prefix = RedisCacheTests.NewPrefix();
        await using var host = await CacheTestHost.CreateAsync(factory.RedisConnectionString, prefix,
            _ => Task.FromResult<string?>("unreachable"), Ct, waitSeconds: 1);
        await host.Connection.GetDatabase().StringSetAsync(Key.LeaseKey(prefix), "dead-owner", TimeSpan.FromSeconds(60));
        await Assert.ThrowsAsync<TimeoutException>(() => RedisCacheTests.ReadAsync(host));
        Assert.Equal(0, host.Probe.Calls);
    }

    private Task<CacheTestHost> HostAsync(string prefix, Func<CancellationToken, Task<string?>> read) =>
        CacheTestHost.CreateAsync(factory.RedisConnectionString, prefix, read, Ct);
}
