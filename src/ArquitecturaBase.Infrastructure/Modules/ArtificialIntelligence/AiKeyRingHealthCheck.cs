using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence;

/// <summary>
/// "ai-key-ring" in the readiness (<c>/health</c>, I17). The first time it compares the ring with the control values in the database
/// (the first copy writes them) and keeps the verdict for the life of the process, because the ring is read at startup. A mismatch is
/// Unhealthy, with a log that names the key id, never its value nor the control. If the database does not answer, it keeps nothing and
/// tries again on the next probe: the database check already reports that. Without the "live" tag, like WhatsApp: restarting does not
/// fix a wrong ring. It neither decrypts data nor calls the provider.
/// </summary>
internal sealed partial class AiKeyRingHealthCheck(IServiceScopeFactory scopes, ILogger<AiKeyRingHealthCheck> logger) : IHealthCheck
{
    public const string Name = "ai-key-ring";
    private IReadOnlyList<string>? mismatched;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref mismatched) is { } known) return Verdict(known);
        IReadOnlyList<string> found;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            found = (await scope.ServiceProvider.GetRequiredService<IAiKeyRingControlService>().CheckAsync(cancellationToken)).Value;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("The AI key ring could not be checked against the database yet ("
                + exception.GetType().Name + "); the next probe retries.");
        }
        if (Interlocked.CompareExchange(ref mismatched, found, null) is null && found.Count > 0) LogRingMismatch(logger, Keys(found));
        return Verdict(Volatile.Read(ref mismatched)!);
    }

    private static HealthCheckResult Verdict(IReadOnlyList<string> mismatched) => mismatched.Count == 0
        ? HealthCheckResult.Healthy()
        : HealthCheckResult.Unhealthy("The AI key ring of this copy does not match the one the other copies use for the key "
            + Keys(mismatched) + ". Load the same ArtificialIntelligence:SecretProtection (InstallationId and keys) as the other copies "
            + "and restart the Api, which reads it at startup.");

    private static string Keys(IReadOnlyList<string> keyIds) => string.Join(", ", keyIds.Select(keyId => "'" + keyId + "'"));

    [LoggerMessage(Level = LogLevel.Error, Message = "The AI key ring of this copy does not match the one the other copies use for the key {KeyIds}: "
        + "this copy stays out of readiness. Load the same ArtificialIntelligence:SecretProtection (InstallationId and keys) as the other copies and restart it.")]
    private static partial void LogRingMismatch(ILogger logger, string keyIds);
}
