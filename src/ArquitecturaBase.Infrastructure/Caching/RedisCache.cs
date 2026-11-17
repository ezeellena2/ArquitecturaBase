using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ArquitecturaBase.Infrastructure.Caching;

/// <summary>
/// The sole data-cache adapter. Factories run in an independent scope; a Redis lease coordinates fills across hosts.
/// Invalidation and lease expiry prevent an obsolete owner from publishing or releasing another owner's lease.
/// </summary>
internal sealed partial class RedisCache(
    IConnectionMultiplexer connection,
    IServiceScopeFactory scopes,
    IOptions<RedisCacheOptions> options,
    ILogger<RedisCache> logger)
{
    internal const string MeterName = "ArquitecturaBase.Caching";

    /// <summary>
    /// Redis did not answer: no connection, a command that timed out (<see cref="RedisTimeoutException"/> is a
    /// <see cref="TimeoutException"/>), a server error, or the fill wait running out. Only callers that can go on without
    /// the cache catch it (N9): the settings read the database and the seed keeps starting. A cancellation the caller's
    /// token did not cause (the client cancels the pending commands when it drops a hung socket) is Redis not answering
    /// too: every public operation turns it into a <see cref="TimeoutException"/>. A command the client fails with its
    /// pipe's exception when it drops that socket is too: <c>CommandAsync</c> turns it into a
    /// <see cref="RedisConnectionException"/>.
    /// </summary>
    internal static bool IsUnavailable(Exception exception) => exception is RedisException or TimeoutException;

    public async Task<bool> TryAcquireExclusiveLeaseAsync(RedisCacheKey key, string owner, TimeSpan duration, CancellationToken cancellationToken)
    {
        key.Validate();
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        try
        {
            return await CommandAsync(connection.GetDatabase().StringSetAsync(
                key.LeaseKey(options.Value.KeyPrefix), owner, duration, When.NotExists), cancellationToken);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw NotAnswered(exception);
        }
    }

    public async Task<bool> RenewExclusiveLeaseAsync(RedisCacheKey key, string owner, TimeSpan duration, CancellationToken cancellationToken)
    {
        key.Validate();
        try
        {
            return (long)await CommandAsync(connection.GetDatabase().ScriptEvaluateAsync(RedisCacheScripts.RenewExclusiveLease,
                [key.LeaseKey(options.Value.KeyPrefix)], [owner, (long)duration.TotalMilliseconds]), cancellationToken) == 1;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw NotAnswered(exception);
        }
    }

    public async Task ReleaseExclusiveLeaseAsync(RedisCacheKey key, string owner, TimeSpan cooldown, CancellationToken cancellationToken)
    {
        key.Validate();
        try
        {
            await CommandAsync(connection.GetDatabase().ScriptEvaluateAsync(RedisCacheScripts.ReleaseExclusiveLease,
                [key.LeaseKey(options.Value.KeyPrefix)], [owner, (long)cooldown.TotalMilliseconds]), cancellationToken);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw NotAnswered(exception);
        }
    }

    private static readonly Meter CacheMeter = new(MeterName);
    private static readonly Counter<long> Hits = CacheMeter.CreateCounter<long>("cache.hits");
    private static readonly Counter<long> Misses = CacheMeter.CreateCounter<long>("cache.misses");
    private static readonly Histogram<double> FactoryDuration = CacheMeter.CreateHistogram<double>("cache.factory.duration", "s");
    private static readonly Histogram<double> WaitDuration = CacheMeter.CreateHistogram<double>("cache.lease.wait.duration", "s");
    private static readonly Histogram<long> PayloadSize = CacheMeter.CreateHistogram<long>("cache.payload.size", "By");

    public Task<TValue> GetOrCreateInOwnScopeAsync<TReader, TState, TValue>(
        RedisCacheKey key,
        TState state,
        Func<TReader, TState, CancellationToken, Task<TValue>> read,
        TimeSpan expiration,
        CancellationToken cancellationToken)
        where TReader : notnull =>
        GetOrCreateInOwnScopeAsync(key, state, read, expiration, static _ => true, cancellationToken);

    /// <summary>
    /// The same read, but only a value <paramref name="keep"/> accepts is stored: one it rejects (a failure of an external
    /// provider) is returned without storing it, like an oversized one, and the next miss reads again.
    /// </summary>
    public async Task<TValue> GetOrCreateInOwnScopeAsync<TReader, TState, TValue>(
        RedisCacheKey key,
        TState state,
        Func<TReader, TState, CancellationToken, Task<TValue>> read,
        TimeSpan expiration,
        Func<TValue, bool> keep,
        CancellationToken cancellationToken)
        where TReader : notnull
    {
        key.Validate();
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(keep);
        if (expiration <= TimeSpan.Zero || expiration.TotalMilliseconds > long.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(expiration));
        }

        var settings = options.Value;
        var database = connection.GetDatabase();
        RedisKey dataKey = key.DataKey(settings.KeyPrefix);
        RedisKey leaseKey = key.LeaseKey(settings.KeyPrefix);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(settings.WaitSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var token = linked.Token;
        var started = Stopwatch.GetTimestamp();
        var missed = false;

        try
        {
            while (true)
            {
                var cached = await CommandAsync(database.StringGetAsync(dataKey), token);
                if (cached.HasValue)
                {
                    Hits.Add(1);
                    if (missed)
                    {
                        WaitDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds);
                    }
                    return Deserialize<TValue>(cached, settings.MaximumPayloadBytes);
                }

                if (!missed)
                {
                    Misses.Add(1);
                    missed = true;
                }

                var owner = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
                if (await CommandAsync(database.StringSetAsync(
                    leaseKey, owner, TimeSpan.FromSeconds(settings.LeaseSeconds), When.NotExists), token))
                {
                    try
                    {
                        WaitDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds);
                        // Another owner may have published between our miss and acquiring the lease.
                        cached = await CommandAsync(database.StringGetAsync(dataKey), token);
                        if (cached.HasValue)
                        {
                            Hits.Add(1);
                            return Deserialize<TValue>(cached, settings.MaximumPayloadBytes);
                        }

                        await using var scope = scopes.CreateAsyncScope();
                        var factoryStarted = Stopwatch.GetTimestamp();
                        TValue value;
                        try
                        {
                            value = await read(scope.ServiceProvider.GetRequiredService<TReader>(), state, token);
                        }
                        finally
                        {
                            FactoryDuration.Record(Stopwatch.GetElapsedTime(factoryStarted).TotalSeconds);
                        }
                        token.ThrowIfCancellationRequested();
                        if (!keep(value))
                        {
                            return value;
                        }

                        var payload = JsonSerializer.SerializeToUtf8Bytes(new CacheValue<TValue>(value));
                        PayloadSize.Record(payload.Length);

                        // Large results are returned without storing them; the lease is still released.
                        if (payload.Length <= settings.MaximumPayloadBytes)
                        {
                            await CommandAsync(database.ScriptEvaluateAsync(RedisCacheScripts.Publish,
                                [dataKey, leaseKey], [owner, payload, (long)Math.Ceiling(expiration.TotalMilliseconds)]), token);
                        }

                        return value;
                    }
                    finally
                    {
                        // Cleanup is independent of request cancellation. Redis's command timeout bounds it; if Redis does
                        // not answer (a timeout, or the command cancelled or failed with a hung socket), the lease just
                        // expires.
                        try
                        {
                            await CommandAsync(
                                database.ScriptEvaluateAsync(RedisCacheScripts.Release, [leaseKey], [owner]), CancellationToken.None);
                        }
                        catch (Exception exception) when (exception is RedisException or TimeoutException or OperationCanceledException)
                        {
                            LeaseReleaseFailed(logger);
                        }
                    }
                }

                await Task.Delay(settings.RetryMilliseconds, token);
            }
        }
        // The fill wait ran out, or the client cancelled a command with a hung socket: Redis did not answer either way.
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw NotAnswered(exception);
        }
    }

    public async Task RemoveAsync(RedisCacheKey key, CancellationToken cancellationToken)
    {
        key.Validate();
        var settings = options.Value;
        try
        {
            await CommandAsync(connection.GetDatabase().ScriptEvaluateAsync(RedisCacheScripts.Invalidate,
                [key.DataKey(settings.KeyPrefix), key.LeaseKey(settings.KeyPrefix)]), cancellationToken);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw NotAnswered(exception);
        }
    }

    /// <summary>
    /// A command of the client, bounded by <paramref name="cancellationToken"/>. When the client drops a hung socket ("Dead
    /// socket detected"), the command it was waiting for can fail with the exception of its pipe ("Reading is not allowed
    /// after reader was completed", an <see cref="InvalidOperationException"/>) instead of a
    /// <see cref="RedisConnectionException"/>. Redis did not answer it either, so it becomes one.
    /// </summary>
    private static async Task<T> CommandAsync<T>(Task<T> command, CancellationToken cancellationToken)
    {
        try
        {
            return await command.WaitAsync(cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            throw new RedisConnectionException(
                ConnectionFailureType.SocketClosed, "The Redis connection was dropped while the command was waiting.", exception);
        }
    }

    private static TimeoutException NotAnswered(OperationCanceledException exception) =>
        new("Timed out waiting for the Redis cache operation.", exception);

    private static TValue Deserialize<TValue>(RedisValue cached, int maximumPayloadBytes)
    {
        byte[] payload = (byte[])cached!;
        if (payload.Length > maximumPayloadBytes)
        {
            throw new InvalidOperationException("The Redis cache payload exceeds the configured size limit.");
        }

        return (JsonSerializer.Deserialize<CacheValue<TValue>>(payload)
            ?? throw new InvalidOperationException("The Redis cache payload is invalid.")).Value;
    }

    // Wrapping the value distinguishes a cached null from a cache miss.
    private sealed record CacheValue<TValue>(TValue Value);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The cache lease could not be released; it will expire automatically.")]
    private static partial void LeaseReleaseFailed(ILogger logger);
}
