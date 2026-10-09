using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;

internal sealed class AiAssistantTransactionProbe : DbTransactionInterceptor
{
    private readonly AsyncLocal<Observation?> _observation = new();
    public int ActiveTransactions => _observation.Value?.Transactions.Count ?? 0;
    public int StartedTransactions => _observation.Value?.Started ?? 0;

    public IDisposable ObserveFlow()
    {
        var previous = _observation.Value;
        _observation.Value = new Observation();
        return new Scope(this, previous);
    }

    public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection,
        TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
    {
        if (_observation.Value is { } observed)
        {
            observed.Transactions.TryAdd(eventData.TransactionId, 0);
            Interlocked.Increment(ref observed.Started);
        }
        return ValueTask.FromResult(result);
    }

    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        _observation.Value?.Transactions.TryRemove(eventData.TransactionId, out _);
        return Task.CompletedTask;
    }

    public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        _observation.Value?.Transactions.TryRemove(eventData.TransactionId, out _);
        return Task.CompletedTask;
    }

    private sealed class Observation
    {
        public ConcurrentDictionary<Guid, byte> Transactions { get; } = new();
        public int Started;
    }

    private sealed class Scope(AiAssistantTransactionProbe owner, Observation? previous) : IDisposable
    {
        public void Dispose() => owner._observation.Value = previous;
    }
}
