using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.Modules.ArtificialIntelligence;

// I9: the slots of Home's uploads in this copy, one instance per copy. First one of the person's, with no queue, so a second upload of
// the same person is refused at once; then one of the copy's UploadConcurrencyLimit, with UploadQueueLimit waiting for one. A refused
// lease frees what it took; a granted one holds both until it is disposed.
public sealed class AiAssistantUploadLimiter : IDisposable
{
    private const string Copy = "ai-assistant-uploads";
    private readonly PartitionedRateLimiter<Guid> people = PartitionedRateLimiter.Create<Guid, Guid>(personId =>
        RateLimitPartition.GetConcurrencyLimiter(personId, _ => new ConcurrencyLimiterOptions { PermitLimit = 1, QueueLimit = 0 }));
    private readonly PartitionedRateLimiter<Guid> copy;
    private readonly PartitionedRateLimiter<Guid> both;

    public AiAssistantUploadLimiter(IOptions<AiRateLimitOptions> options)
    {
        var settings = options.Value;
        copy = PartitionedRateLimiter.Create<Guid, string>(_ => RateLimitPartition.GetConcurrencyLimiter(Copy, _ => new ConcurrencyLimiterOptions
        {
            PermitLimit = settings.UploadConcurrencyLimit, QueueLimit = settings.UploadQueueLimit, QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        }));
        both = PartitionedRateLimiter.CreateChained(people, copy);
    }

    public ValueTask<RateLimitLease> AcquireAsync(Guid personId, CancellationToken cancellationToken) =>
        both.AcquireAsync(personId, 1, cancellationToken);

    public void Dispose()
    {
        both.Dispose();
        copy.Dispose();
        people.Dispose();
    }
}
