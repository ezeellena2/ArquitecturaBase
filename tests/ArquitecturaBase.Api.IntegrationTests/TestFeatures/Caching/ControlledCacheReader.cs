namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Caching;

internal sealed class CacheReadProbe(Func<CancellationToken, Task<string?>> read)
{
    private int _calls;
    public int Calls => Volatile.Read(ref _calls);
    public List<ControlledCacheReader> Readers { get; } = [];

    public Task<string?> ReadAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        return read(cancellationToken);
    }
}

internal sealed class ControlledCacheReader : IDisposable
{
    private readonly CacheReadProbe _probe;

    public ControlledCacheReader(CacheReadProbe probe)
    {
        _probe = probe;
        lock (probe.Readers)
        {
            probe.Readers.Add(this);
        }
    }

    public bool Disposed { get; private set; }
    public Task<string?> ReadAsync(CancellationToken cancellationToken) => _probe.ReadAsync(cancellationToken);
    public void Dispose() => Disposed = true;
}
