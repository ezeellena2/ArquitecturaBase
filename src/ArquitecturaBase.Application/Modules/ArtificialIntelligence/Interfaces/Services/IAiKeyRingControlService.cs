using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
/// <summary>I17: compares the configured key ring with the control values in the database, and writes those still missing.</summary>
public interface IAiKeyRingControlService
{
    /// <summary>The identifiers of the keys whose control differs; empty when the ring matches.</summary>
    Task<Result<IReadOnlyList<string>>> CheckAsync(CancellationToken cancellationToken);
}
