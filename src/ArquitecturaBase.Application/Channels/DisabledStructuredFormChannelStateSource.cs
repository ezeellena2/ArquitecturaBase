using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Channels;

internal sealed class DisabledStructuredFormChannelStateSource : IStructuredFormChannelStateSource
{
    public Task<IReadOnlyList<StructuredFormChannelStateRow>> ListAsync(IReadOnlyCollection<Guid> definitionIds, DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<StructuredFormChannelStateRow>>([]);
    }
}
