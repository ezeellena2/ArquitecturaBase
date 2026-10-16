using ArquitecturaBase.Application.Interfaces.Channels;
namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Files;
internal sealed class MemoryPrivateFileRetentionSource : IPrivateFileRetentionSource
{
    public HashSet<string> Retained { get; } = new(StringComparer.Ordinal);
    public List<IReadOnlyList<string>> Requests { get; } = [];
    public Exception? Failure { get; set; }
    public Task<IReadOnlyList<string>> ListReferencedKeysAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken)
    {
        Requests.Add(keys.ToArray());
        if (Failure is not null) throw Failure;
        return Task.FromResult<IReadOnlyList<string>>(keys.Where(Retained.Contains).ToArray());
    }
}
