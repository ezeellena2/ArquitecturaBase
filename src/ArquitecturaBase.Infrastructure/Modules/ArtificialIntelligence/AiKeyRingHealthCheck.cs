using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence;

/// <summary>
/// "ai-key-ring" in the readiness (<c>/health</c>, I17). It asks <see cref="AiKeyRingReadiness"/>, which compares the ring with the
/// control values in the database the first time (the first copy writes them) and keeps the verdict for the life of the process; the
/// worker and the channels ask the same one (I17, review). A mismatch is Unhealthy, naming the key id, never its value nor the control.
/// If the database does not answer, nothing is kept and the next probe retries: the database check already reports that. Without the
/// "live" tag, like WhatsApp: restarting does not fix a wrong ring. It neither decrypts data nor calls the provider.
/// </summary>
internal sealed class AiKeyRingHealthCheck(AiKeyRingReadiness readiness) : IHealthCheck
{
    public const string Name = "ai-key-ring";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> mismatched;
        try { mismatched = await readiness.CheckAsync(cancellationToken); }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("The AI key ring could not be checked against the database yet ("
                + exception.GetType().Name + "); the next probe retries.");
        }
        return mismatched.Count == 0
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("The AI key ring of this copy does not match the one the other copies use for the key "
                + AiKeyRingReadiness.Keys(mismatched) + ". Load the same ArtificialIntelligence:SecretProtection (InstallationId and keys) as the other copies "
                + "and restart the Api, which reads it at startup.");
    }
}
