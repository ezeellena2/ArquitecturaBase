using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence;

/// <summary>
/// I17, review: the verdict of this copy's key ring against the control values in the database, computed the first time someone needs
/// it (the readiness, the worker's claim, the channels' availability or an acceptance) and kept for the life of the process, because
/// the ring is read at startup. While the database does not answer, nothing is kept and the next call tries again; until then the copy
/// is not ready either. A mismatch is logged once, naming the key ids, never a key nor a control.
/// </summary>
internal sealed partial class AiKeyRingReadiness(IServiceScopeFactory scopes, ILogger<AiKeyRingReadiness> logger) : IAiKeyRingReadiness
{
    private IReadOnlyList<string>? mismatched;

    public async Task<bool> MatchesAsync(CancellationToken cancellationToken)
    {
        try { return (await CheckAsync(cancellationToken)).Count == 0; }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogNotVerified(logger, exception.GetType().Name);
            return false;
        }
    }

    /// <summary>The keys whose control differs, empty when the ring matches; it throws, keeping nothing, while the database does not answer.</summary>
    public async Task<IReadOnlyList<string>> CheckAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref mismatched) is { } known) return known;
        IReadOnlyList<string> found;
        await using (var scope = scopes.CreateAsyncScope())
            found = (await scope.ServiceProvider.GetRequiredService<IAiKeyRingControlService>().CheckAsync(cancellationToken)).Value;
        if (Interlocked.CompareExchange(ref mismatched, found, null) is null && found.Count > 0) LogRingMismatch(logger, Keys(found));
        return Volatile.Read(ref mismatched)!;
    }

    public static string Keys(IReadOnlyList<string> keyIds) => string.Join(", ", keyIds.Select(keyId => "'" + keyId + "'"));

    [LoggerMessage(Level = LogLevel.Error, Message = "The AI key ring of this copy does not match the one the other copies use for the key {KeyIds}: "
        + "this copy stays out of readiness. Load the same ArtificialIntelligence:SecretProtection (InstallationId and keys) as the other copies and restart it.")]
    private static partial void LogRingMismatch(ILogger logger, string keyIds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The AI key ring could not be checked against the database yet ({ExceptionType}); "
        + "this copy takes no assistant work until it can.")]
    private static partial void LogNotVerified(ILogger logger, string exceptionType);
}
