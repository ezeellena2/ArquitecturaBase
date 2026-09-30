namespace ArquitecturaBase.Infrastructure.Caching;

internal static class RedisCacheScripts
{
    // Removing the lease also fences factories that started before invalidation.
    public const string Invalidate = "return redis.call('DEL', KEYS[1], KEYS[2])";

    public const string Publish = """
        if redis.call('GET', KEYS[2]) == ARGV[1] then
            redis.call('SET', KEYS[1], ARGV[2], 'PX', ARGV[3])
            redis.call('DEL', KEYS[2])
            return 1
        end
        return 0
        """;

    public const string Release = """
        if redis.call('GET', KEYS[1]) == ARGV[1] then
            return redis.call('DEL', KEYS[1])
        end
        return 0
        """;
}
