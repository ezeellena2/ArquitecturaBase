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
    private static readonly Meter CacheMeter = new(MeterName);
    private static readonly Counter<long> Hits = CacheMeter.CreateCounter<long>("cache.hits");
    private static readonly Counter<long> Misses = CacheMeter.CreateCounter<long>("cache.misses");
    private static readonly Histogram<double> FactoryDuration = CacheMeter.CreateHistogram<double>("cache.factory.duration", "s");
    private static readonly Histogram<double> WaitDuration = CacheMeter.CreateHistogram<double>("cache.lease.wait.duration", "s");
    private static readonly Histogram<long> PayloadSize = CacheMeter.CreateHistogram<long>("cache.payload.size", "By");

    public async Task<TValue> GetOrCreateInOwnScopeAsync<TReader, TState, TValue>(
        RedisCacheKey key,
        TState state,
        Func<TReader, TState, CancellationToken, Task<TValue>> read,
        TimeSpan expiration,
        CancellationToken cancellationToken)
        where TReader : notnull
    {
        key.Validate();
        ArgumentNullException.ThrowIfNull(read);
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
                var cached = await database.StringGetAsync(dataKey).WaitAsync(token);
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
                if (await database.StringSetAsync(
                    leaseKey, owner, TimeSpan.FromSeconds(settings.LeaseSeconds), When.NotExists).WaitAsync(token))
                {
                    try
                    {
                        WaitDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds);
                        // Another owner may have published between our miss and acquiring the lease.
                        cached = await database.StringGetAsync(dataKey).WaitAsync(token);
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
                        var payload = JsonSerializer.SerializeToUtf8Bytes(new CacheValue<TValue>(value));
                        PayloadSize.Record(payload.Length);

                        // Large results are returned without storing them; the lease is still released.
                        if (payload.Length <= settings.MaximumPayloadBytes)
                        {
                            await database.ScriptEvaluateAsync(RedisCacheScripts.Publish,
                                [dataKey, leaseKey], [owner, payload, (long)Math.Ceiling(expiration.TotalMilliseconds)])
                                .WaitAsync(token);
                        }

                        return value;
                    }
                    finally
                    {
                        // Cleanup is independent of request cancellation. Redis's command timeout bounds it.
                        try
                        {
                            await database.ScriptEvaluateAsync(RedisCacheScripts.Release, [leaseKey], [owner]);
                        }
                        catch (RedisException)
                        {
                            LeaseReleaseFailed(logger);
                        }
                    }
                }

                await Task.Delay(settings.RetryMilliseconds, token);
            }
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new TimeoutException("Timed out waiting for the Redis cache operation.", exception);
        }
    }

    public async Task RemoveAsync(RedisCacheKey key, CancellationToken cancellationToken)
    {
        key.Validate();
        var settings = options.Value;
        await connection.GetDatabase().ScriptEvaluateAsync(RedisCacheScripts.Invalidate,
            [key.DataKey(settings.KeyPrefix), key.LeaseKey(settings.KeyPrefix)])
            .WaitAsync(cancellationToken);
    }

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
