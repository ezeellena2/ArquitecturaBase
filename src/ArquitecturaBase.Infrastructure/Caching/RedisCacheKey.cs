namespace ArquitecturaBase.Infrastructure.Caching;

/// <summary>Logical key. Its data and lease share a hash slot, including on Redis Cluster.</summary>
internal readonly record struct RedisCacheKey(string Value)
{
    public string DataKey(string prefix) => $"{prefix}:v1:{{{Value}}}:data";

    public string LeaseKey(string prefix) => $"{prefix}:v1:{{{Value}}}:lease";

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Value);
        if (Value.Length > 512 || Value.Contains('{', StringComparison.Ordinal) || Value.Contains('}', StringComparison.Ordinal))
        {
            throw new ArgumentException("Cache keys must be at most 512 characters and cannot contain hash-tag delimiters.");
        }
    }
}
