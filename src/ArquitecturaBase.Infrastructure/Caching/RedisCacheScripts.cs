namespace ArquitecturaBase.Infrastructure.Caching;

internal static class RedisCacheScripts
{
    public const string RenewExclusiveLease = """
        if redis.call('GET', KEYS[1]) == ARGV[1] then
            return redis.call('PEXPIRE', KEYS[1], ARGV[2])
        end
        return 0
        """;
    public const string ReleaseExclusiveLease = """
        if redis.call('GET', KEYS[1]) == ARGV[1] then
            if tonumber(ARGV[2]) > 0 then
                redis.call('SET', KEYS[1], 'cooldown', 'PX', ARGV[2])
                return 1
            end
            return redis.call('DEL', KEYS[1])
        end
        return 0
        """;
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
