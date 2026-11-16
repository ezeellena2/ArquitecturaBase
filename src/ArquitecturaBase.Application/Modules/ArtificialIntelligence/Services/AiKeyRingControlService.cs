using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

/// <summary>
/// I17: compares each key of the configured ring that already has a control in the database; a key without one gets it, and a
/// control whose key left the ring is not compared (a finished rotation). Once there are controls, the active key must already have
/// its own (I17, review): a ring of other keys has nothing to compare, but it would encrypt with a key the other copies do not have. A
/// rotation brings the new key in as a non-active one first, which writes its control. A copy with a key that differs writes nothing,
/// so it cannot claim the other keys of its ring. The lock serializes two copies that start at once: the second reads the first's.
/// </summary>
internal sealed class AiKeyRingControlService(IAiKeyRingControlRepository repository, IAiKeyRingControl ring, IUnitOfWork unitOfWork,
    TimeProvider timeProvider, ILogger<AiKeyRingControlService> logger) : IAiKeyRingControlService
{
    // I17, review: like every public method that returns a Result (backend.md). It is no per-second poll: the copy keeps the first
    // verdict, so it runs once per process while the database answers.
    public Task<Result<IReadOnlyList<string>>> CheckAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<IReadOnlyList<string>>>(logger, "CheckAiKeyRing", () =>
            unitOfWork.ExecuteInTransactionAsync(CheckCoreAsync, CommitPolicy.OnSuccess, cancellationToken));

    private async Task<Result<IReadOnlyList<string>>> CheckCoreAsync(CancellationToken ct)
    {
        await repository.LockAsync(ct);
        var stored = (await repository.ListAsync(ct)).ToDictionary(control => control.KeyId, control => control.Control, StringComparer.Ordinal);
        var controls = ring.CreateControls();
        IReadOnlyList<string> mismatched = [.. controls
            .Where(control => stored.TryGetValue(control.Key, out var value) ? !string.Equals(value, control.Value, StringComparison.Ordinal)
                : stored.Count > 0 && string.Equals(control.Key, ring.ActiveKeyId, StringComparison.Ordinal))
            .Select(control => control.Key).Order(StringComparer.Ordinal)];
        if (mismatched.Count > 0) return Result.Success(mismatched);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var (keyId, control) in controls.Where(control => !stored.ContainsKey(control.Key)).OrderBy(control => control.Key, StringComparer.Ordinal))
            repository.Add(AiKeyRingControl.Create(keyId, control, now));
        return Result.Success(mismatched);
    }
}
