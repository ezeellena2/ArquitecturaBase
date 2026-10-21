using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Interfaces.Channels;

/// <summary>Reads owner-confirmed unused versions and shares its selection lock with a channel's transaction.</summary>
public interface IStructuredFormRetirementSource
{
    Task LockSelectionAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<StructuredFormVersionReference>> ListUnusedAsync(IReadOnlyCollection<StructuredFormVersionReference> candidates,
        CancellationToken cancellationToken);
}
