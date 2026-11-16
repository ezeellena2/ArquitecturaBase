using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

/// <summary>
/// I17: compares each key of the configured ring that already has a control in the database; a key without one gets it, and a
/// control whose key left the ring is not compared (a finished rotation). A copy with a key that differs writes nothing, so it
/// cannot claim the other keys of its ring. The lock serializes two copies that start at once: the second reads the first's.
/// </summary>
internal sealed class AiKeyRingControlService(IAiKeyRingControlRepository repository, IAiKeyRingControl ring, IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IAiKeyRingControlService
{
    // A probe of the readiness until it has a verdict: it does not go through OperationLog.
    public Task<Result<IReadOnlyList<string>>> CheckAsync(CancellationToken cancellationToken) =>
        unitOfWork.ExecuteInTransactionAsync(CheckCoreAsync, CommitPolicy.OnSuccess, cancellationToken);

    private async Task<Result<IReadOnlyList<string>>> CheckCoreAsync(CancellationToken ct)
    {
        await repository.LockAsync(ct);
        var stored = (await repository.ListAsync(ct)).ToDictionary(control => control.KeyId, control => control.Control, StringComparer.Ordinal);
        var controls = ring.CreateControls();
        IReadOnlyList<string> mismatched = [.. controls
            .Where(control => stored.TryGetValue(control.Key, out var value) && !string.Equals(value, control.Value, StringComparison.Ordinal))
            .Select(control => control.Key).Order(StringComparer.Ordinal)];
        if (mismatched.Count > 0) return Result.Success(mismatched);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var (keyId, control) in controls.Where(control => !stored.ContainsKey(control.Key)).OrderBy(control => control.Key, StringComparer.Ordinal))
            repository.Add(AiKeyRingControl.Create(keyId, control, now));
        return Result.Success(mismatched);
    }
}
