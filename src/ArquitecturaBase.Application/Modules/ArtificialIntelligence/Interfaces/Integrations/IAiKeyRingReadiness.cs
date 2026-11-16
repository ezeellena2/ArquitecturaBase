namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
/// <summary>
/// I17, review: whether this copy's key ring matches the control values in the database. The readiness is not the only one that asks:
/// the worker takes no turn, and the channels' availability and every acceptance take nothing, while it does not match or could not
/// be verified yet.
/// </summary>
public interface IAiKeyRingReadiness
{
    /// <summary>
    /// Verifies the ring the first time and keeps the verdict for the life of the process; false when it does not match and while the
    /// database did not answer (the next call tries again).
    /// </summary>
    Task<bool> MatchesAsync(CancellationToken cancellationToken);
}
