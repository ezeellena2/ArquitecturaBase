using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Interfaces.Channels;

/// <summary>Reads local channel evidence by definition without writing or calling a provider.</summary>
public interface IStructuredFormChannelStateSource
{
    Task<IReadOnlyList<StructuredFormChannelStateRow>> ListAsync(IReadOnlyCollection<Guid> definitionIds, DateTime nowUtc,
        CancellationToken cancellationToken);
}
